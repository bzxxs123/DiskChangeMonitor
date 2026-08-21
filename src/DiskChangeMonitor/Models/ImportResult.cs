using System.Collections.Generic;

namespace DiskChangeMonitor.Models
{
    public sealed record ImportSummary(long Rows, long IgnoredRows, IReadOnlyList<string> Warnings);

    public sealed record ImportProgress(long BytesRead, long TotalBytes, long Rows, string Stage);

    public sealed record ImportResult(SnapshotMetadata Metadata, ImportSummary Summary, DiffReport Comparison);
}
