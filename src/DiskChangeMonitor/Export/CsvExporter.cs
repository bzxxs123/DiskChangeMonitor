using System.Collections.Generic;
using System.IO;
using System.Text;
using DiskChangeMonitor.Models;

namespace DiskChangeMonitor.Export
{
    /// <summary>Writes a diff report to UTF-8 CSV with RFC-4180 quoting.</summary>
    public static class CsvExporter
    {
        private static readonly string[] Header =
        {
            "变化类型", "路径", "变化前路径", "变化后路径",
            "逻辑大小变化", "分配空间变化", "变化前大小", "变化后大小", "变化前分配", "变化后分配", "目录"
        };

        public static void Export(string filePath, DiffReport report)
        {
            using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Export(writer, report);
        }

        public static void Export(TextWriter writer, DiffReport report)
        {
            WriteRow(writer, Header);
            foreach (var change in report.Changes)
            {
                WriteRow(writer, new[]
                {
                    change.KindText,
                    change.Path,
                    change.OldPath ?? string.Empty,
                    change.NewPath ?? change.Path,
                    change.SizeDelta.ToString(),
                    change.AllocatedDelta.ToString(),
                    change.OldSize.ToString(),
                    change.NewSize.ToString(),
                    change.OldAllocated.ToString(),
                    change.NewAllocated.ToString(),
                    GetDirectory(change.Path)
                });
            }
        }

        private static string GetDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(directory) ? path : directory;
        }

        private static void WriteRow(TextWriter writer, IReadOnlyList<string> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    writer.Write(',');
                }

                WriteField(writer, fields[i]);
            }

            writer.Write("\r\n");
        }

        private static void WriteField(TextWriter writer, string field)
        {
            if (field.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                writer.Write('"');
                writer.Write(field.Replace("\"", "\"\""));
                writer.Write('"');
            }
            else
            {
                writer.Write(field);
            }
        }
    }
}
