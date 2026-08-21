using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DiskChangeMonitor.Models;

namespace DiskChangeMonitor.Import
{
    /// <summary>A parsed row from a WizTree CSV export.</summary>
    /// <param name="RowNumber">1-based CSV row number (header is row 1).</param>
    /// <param name="Entry">The parsed entry, or null when the row was skipped.</param>
    /// <param name="Warning">Why the row was skipped, or null when parsed successfully.</param>
    public sealed record ParsedRow(long RowNumber, FileEntry? Entry, string? Warning);

    /// <summary>
    /// Streams WizTree 4.28 CSV exports (UTF-8, optional BOM, quoted Chinese paths,
    /// scientific-notation numbers, localized dates, blank folder fields). The header
    /// is validated in the constructor so callers can reject an import before any
    /// data is written.
    /// </summary>
    public sealed class WizTreeCsvParser
    {
        private const int ProgressReportRows = 10_000;

        private static readonly string[] RequiredColumns = { "文件名称", "大小", "分配", "修改时间", "属性" };

        private static readonly string[] DateFormats =
        {
            "yyyy/M/d H:mm:ss",
            "yyyy/M/d H:mm",
            "yyyy/M/d H",
            "yyyy/M/d",
            "yyyy/M/d h:mm:ss tt",
            "yyyy/M/d h:mm tt",
            "yyyy/M/d h tt",
            "yyyy-MM-dd H:mm:ss",
            "yyyy-MM-dd H:mm",
            "yyyy-MM-dd"
        };

        private static readonly CultureInfo ZhCn = CultureInfo.GetCultureInfo("zh-CN");

        private readonly StreamReader _reader;
        private readonly Stream _stream;
        private readonly IProgress<ImportProgress>? _progress;
        private readonly long _totalBytes;
        private readonly Dictionary<string, int> _columnIndex = new(StringComparer.Ordinal);
        private long _dataRowCount;

        public WizTreeCsvParser(Stream stream, IProgress<ImportProgress>? progress = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _progress = progress;
            _totalBytes = stream.CanSeek ? stream.Length : 0;
            _reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1 << 16,
                leaveOpen: true);
            ReadHeader();
        }

        public IReadOnlyList<string> Header { get; private set; } = Array.Empty<string>();

        private void ReadHeader()
        {
            var firstRow = new CsvFieldParser(_reader).ReadRows().FirstOrDefault();
            if (firstRow == null)
            {
                throw new CsvHeaderException("CSV 文件为空，未找到表头。");
            }

            var columns = new List<string>(firstRow.Length);
            foreach (var column in firstRow)
            {
                var trimmed = column.Trim();
                columns.Add(trimmed);
                _columnIndex[trimmed] = columns.Count - 1;
            }

            Header = columns;

            var missing = RequiredColumns.Where(column => !_columnIndex.ContainsKey(column)).ToList();
            if (missing.Count > 0)
            {
                throw new CsvHeaderException("缺少必需列: " + string.Join(", ", missing));
            }
        }

        /// <summary>
        /// Streams all data rows after the header. Skipped rows yield a ParsedRow with
        /// a warning and no entry; fully blank lines are skipped silently.
        /// </summary>
        public IEnumerable<ParsedRow> ReadDataRows()
        {
            var parser = new CsvFieldParser(_reader);
            foreach (var fields in parser.ReadRows())
            {
                if (fields.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                _dataRowCount++;
                var rowNumber = 1 + _dataRowCount;
                yield return ParseRow(fields, rowNumber);

                if (_dataRowCount % ProgressReportRows == 0)
                {
                    ReportProgress();
                }
            }

            ReportProgress();
        }

        private ParsedRow ParseRow(string[] fields, long rowNumber)
        {
            var reasons = new List<string>();
            var path = GetField(fields, "文件名称");
            if (path.Length == 0)
            {
                reasons.Add("路径为空");
            }

            var size = ParseLongField(fields, "大小", "大小", reasons);
            var allocated = ParseLongField(fields, "分配", "分配", reasons);
            var modified = ParseDateField(fields, reasons);
            var attributes = GetField(fields, "属性");

            var fileCount = ParseOptionalCount(fields, "文件");
            var folderCount = ParseOptionalCount(fields, "文件夹");
            var isDirectory = IsDirectory(fields, attributes);

            if (reasons.Count > 0)
            {
                return new ParsedRow(rowNumber, null, $"第 {rowNumber} 行已跳过: {string.Join("; ", reasons)}");
            }

            var entry = new FileEntry(path, size, allocated, modified, attributes, isDirectory, fileCount, folderCount);
            return new ParsedRow(rowNumber, entry, null);
        }

        private string GetField(string[] fields, string columnName)
        {
            if (!_columnIndex.TryGetValue(columnName, out var index) || index >= fields.Length)
            {
                return string.Empty;
            }

            return fields[index].Trim();
        }

        private long ParseLongField(string[] fields, string columnName, string label, List<string> reasons)
        {
            var text = GetField(fields, columnName);
            if (TryParseLong(text, out var value))
            {
                return value;
            }

            reasons.Add($"{label}无法解析: “{text}”");
            return 0;
        }

        private DateTime? ParseDateField(string[] fields, List<string> reasons)
        {
            var text = GetField(fields, "修改时间");
            if (text.Length == 0)
            {
                return null;
            }

            if (TryParseDate(text, out var value))
            {
                return value;
            }

            reasons.Add($"修改时间无法解析: “{text}”");
            return null;
        }

        private long? ParseOptionalCount(string[] fields, string columnName)
        {
            var text = GetField(fields, columnName);
            if (text.Length > 0 && TryParseLong(text, out var value))
            {
                return value;
            }

            return null;
        }

        private bool IsDirectory(string[] fields, string attributes)
        {
            if (attributes.IndexOf('D', StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return ParseOptionalCount(fields, "文件") != null || ParseOptionalCount(fields, "文件夹") != null;
        }

        private void ReportProgress()
        {
            if (_progress == null)
            {
                return;
            }

            var bytesRead = _stream.CanSeek ? _stream.Position : 0;
            _progress.Report(new ImportProgress(bytesRead, _totalBytes, _dataRowCount, "解析导入"));
        }

        internal static bool TryParseLong(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (decimal.TryParse(
                    text,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                try
                {
                    value = checked((long)parsed);
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            return false;
        }

        internal static bool TryParseDate(string text, out DateTime value)
        {
            if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return true;
            }

            if (DateTime.TryParseExact(text, DateFormats, ZhCn, DateTimeStyles.None, out value))
            {
                return true;
            }

            return DateTime.TryParse(text, ZhCn, DateTimeStyles.None, out value);
        }
    }
}
