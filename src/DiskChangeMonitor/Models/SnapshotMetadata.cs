using System;

namespace DiskChangeMonitor.Models
{
    /// <summary>Metadata for one completed or staging snapshot.</summary>
    public sealed record SnapshotMetadata(
        string Id,
        string MonitoredRoot,
        DateTime ImportedAt,
        string SourcePath,
        long SourceBytes,
        string Fingerprint,
        long Rows,
        long IgnoredRows,
        string Status)
    {
        public const string Completed = "Completed";
        public const string Staging = "Staging";

        public string DisplayTime => ImportedAt.ToString("yyyy-MM-dd HH:mm:ss");
        public string DisplayRows => Rows.ToString("N0") + " 行";
    }
}
