using System.Collections.Generic;

namespace DiskChangeMonitor.Models
{
    /// <summary>
    /// Result of comparing the latest two completed snapshots. Unchanged items are
    /// counted but not included in <see cref="Changes"/>.
    /// </summary>
    public sealed record DiffReport(
        IReadOnlyList<FileChange> Changes,
        long NewCount,
        long DeletedCount,
        long EnlargedCount,
        long ReducedCount,
        long MovedCount,
        long UnchangedCount,
        long SizeDelta,
        long AllocatedDelta);
}
