using System;
using System.Collections.Generic;

namespace DiskChangeMonitor.Models
{
    /// <summary>
    /// A handle to one snapshot's rows. RowFactory streams rows sorted by path (ordinal),
    /// so a 280 MB export never has to be loaded into memory at once.
    /// </summary>
    public sealed record SnapshotData(
        string SnapshotId,
        string MonitoredRoot,
        Func<IEnumerable<FileEntry>> RowFactory);
}
