using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiskChangeMonitor.Diff;
using DiskChangeMonitor.Models;
using DiskChangeMonitor.Storage;

namespace DiskChangeMonitor.Import
{
    /// <summary>
    /// Orchestrates a manual WizTree CSV import: validates the header, streams the file
    /// once (hashing while parsing), stages rows in SQLite, commits atomically, then
    /// compares the new snapshot against the immediately previous one.
    /// </summary>
    public sealed class ImportCoordinator
    {
        public const int MaxWarnings = 1000;
        private const int ChunkSize = 1000;

        private readonly ISnapshotStore _store;

        public ImportCoordinator(ISnapshotStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Rough estimate of the SQLite space a source CSV will consume.</summary>
        public static long EstimateDatabaseBytes(long sourceBytes)
        {
            return Math.Max(0, sourceBytes * 2);
        }

        public async Task<ImportResult> ImportAsync(
            MonitoredLocation root,
            string csvPath,
            IProgress<ImportProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (string.IsNullOrWhiteSpace(csvPath))
            {
                throw new ArgumentException("CSV 路径不能为空。", nameof(csvPath));
            }

            if (!File.Exists(csvPath))
            {
                throw new FileNotFoundException("找不到 CSV 文件。", csvPath);
            }

            var sourceBytes = new FileInfo(csvPath).Length;
            string? stagingId = null;

            try
            {
                using var sha = SHA256.Create();
                using var fileStream = new FileStream(
                    csvPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1 << 16,
                    FileOptions.SequentialScan);
                using var hashingStream = new CryptoStream(fileStream, sha, CryptoStreamMode.Read);

                progress?.Report(new ImportProgress(0, sourceBytes, 0, "读取表头"));
                var parser = new WizTreeCsvParser(hashingStream, progress);
                progress?.Report(new ImportProgress(0, sourceBytes, 0, "写入数据库"));

                var staging = await _store.BeginImportAsync(
                    root,
                    Path.GetFullPath(csvPath),
                    sourceBytes,
                    fingerprint: string.Empty,
                    ct: ct);
                stagingId = staging.Id;

                long rows = 0;
                long ignored = 0;
                var warnings = new List<string>();
                var chunk = new List<FileEntry>(ChunkSize);

                foreach (var row in parser.ReadDataRows())
                {
                    ct.ThrowIfCancellationRequested();
                    if (row.Entry != null)
                    {
                        chunk.Add(row.Entry);
                        rows++;
                        if (chunk.Count >= ChunkSize)
                        {
                            await _store.AppendRowsAsync(stagingId, chunk, ct);
                            chunk.Clear();
                        }
                    }
                    else
                    {
                        ignored++;
                        if (warnings.Count < MaxWarnings)
                        {
                            warnings.Add(row.Warning ?? $"第 {row.RowNumber} 行被跳过。");
                        }
                    }
                }

                if (chunk.Count > 0)
                {
                    await _store.AppendRowsAsync(stagingId, chunk, ct);
                }

                await _store.UpdateFingerprintAsync(stagingId, ToHex(sha.Hash ?? Array.Empty<byte>()), ct);

                progress?.Report(new ImportProgress(sourceBytes, sourceBytes, rows, "对比快照"));
                var metadata = await _store.CommitAsync(stagingId, rows, ignored, ct);
                stagingId = null;

                var history = await _store.ListAsync(root.RootPath, ct);
                var previous = history.FirstOrDefault(snapshot => snapshot.Id != metadata.Id);
                var comparison = previous == null
                    ? new DiffReport(Array.Empty<FileChange>(), 0, 0, 0, 0, 0, 0, 0, 0)
                    : DiffEngine.Compare(
                        await _store.LoadAsync(previous.Id, ct),
                        await _store.LoadAsync(metadata.Id, ct));

                progress?.Report(new ImportProgress(sourceBytes, sourceBytes, rows, "完成"));
                return new ImportResult(metadata, new ImportSummary(rows, ignored, warnings), comparison);
            }
            catch
            {
                if (stagingId != null)
                {
                    try
                    {
                        await _store.CancelAsync(stagingId, CancellationToken.None);
                    }
                    catch
                    {
                        // Best effort: the staging row is invisible to history either way.
                    }
                }

                throw;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
