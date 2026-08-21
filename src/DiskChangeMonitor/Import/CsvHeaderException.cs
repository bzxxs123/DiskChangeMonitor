using System;

namespace DiskChangeMonitor.Import
{
    /// <summary>Thrown when the CSV header is missing a required WizTree column.</summary>
    public sealed class CsvHeaderException : Exception
    {
        public CsvHeaderException(string message)
            : base(message)
        {
        }
    }
}
