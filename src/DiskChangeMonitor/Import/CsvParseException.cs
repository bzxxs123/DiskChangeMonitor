using System;

namespace DiskChangeMonitor.Import
{
    /// <summary>Thrown when the CSV structure itself is unreadable (e.g. an unclosed quote).</summary>
    public sealed class CsvParseException : Exception
    {
        public int Line { get; }

        public CsvParseException(int line, string message)
            : base($"CSV 第 {line} 行解析失败: {message}")
        {
            Line = line;
        }
    }
}
