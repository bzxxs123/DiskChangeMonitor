using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChangeMonitor.Models;

namespace DiskChangeMonitor.Storage
{
    /// <summary>
    /// Transactional SQLite-backed snapshot store. Snapshots are staged and only become
    /// visible (and comparable) after a successful commit.
    /// </summary>
    public interface ISnapshotStore
    {
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>Creates a staging snapshot. It is invisible to ListAsync until committed.</summary>
        Task<SnapshotMetadata> BeginImportAsync(
            MonitoredLocation root,
            string sourcePath,
            long sourceBytes,
            string fingerprint,
            DateTime? importedAt = null,
            CancellationToken ct = default);

        Task AppendRowsAsync(string snapshotId, IEnumerable<FileEntry> rows, CancellationToken ct = default);

        /// <summary>Finalizes the staging snapshot, builds the path index, and prunes
        /// this root's history to the 5 newest completed snapshots.</summary>
        Task<SnapshotMetadata> CommitAsync(string snapshotId, long parsedRows, long ignoredRows, CancellationToken ct = default);

        /// <summary>Deletes a staging (or any) snapshot and its rows without touching history.</summary>
        Task CancelAsync(string snapshotId, CancellationToken ct = default);

        /// <summary>Completed snapshots for one root, newest first.</summary>
        Task<IReadOnlyList<SnapshotMetadata>> ListAsync(string rootPath, CancellationToken ct = default);

        /// <summary>Loads a snapshot as a streaming, path-sorted row source.</summary>
        Task<SnapshotData> LoadAsync(string snapshotId, CancellationToken ct = default);
    }
}
