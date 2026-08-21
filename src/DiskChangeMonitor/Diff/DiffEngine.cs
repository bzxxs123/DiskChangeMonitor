using System;
using System.Collections.Generic;
using System.Linq;
using DiskChangeMonitor.Models;

namespace DiskChangeMonitor.Diff
{
    /// <summary>
    /// Pure difference engine: merge-walks two path-sorted row streams and classifies
    /// every item as new, deleted, enlarged, reduced, moved, or unchanged. Both logical
    /// size (大小) and allocated space (分配) are compared; allocated space wins when the
    /// two disagree about the direction of change.
    /// </summary>
    public static class DiffEngine
    {
        /// <summary>
        /// When a single comparison has more new+deleted items than this, move/rename
        /// pairing is skipped to keep memory bounded on full-reinstall diffs.
        /// </summary>
        internal static long MovePairingLimit { get; set; } = 200_000;

        public static DiffReport Compare(SnapshotData older, SnapshotData newer)
        {
            if (older == null)
            {
                throw new ArgumentNullException(nameof(older));
            }

            if (newer == null)
            {
                throw new ArgumentNullException(nameof(newer));
            }

            var changes = new List<FileChange>();
            long unchangedCount = 0;

            using var oldEnumerator = older.RowFactory().GetEnumerator();
            using var newEnumerator = newer.RowFactory().GetEnumerator();
            var hasOld = oldEnumerator.MoveNext();
            var hasNew = newEnumerator.MoveNext();

            while (hasOld || hasNew)
            {
                int comparison;
                if (!hasNew)
                {
                    comparison = -1;
                }
                else if (!hasOld)
                {
                    comparison = 1;
                }
                else
                {
                    comparison = StringComparer.Ordinal.Compare(oldEnumerator.Current.Path, newEnumerator.Current.Path);
                }

                if (comparison < 0)
                {
                    var old = oldEnumerator.Current;
                    changes.Add(new FileChange(
                        ChangeKind.Deleted, old.Path, null, null,
                        old.Size, 0, old.Allocated, 0, old.IsDirectory, old.Modified));
                    hasOld = oldEnumerator.MoveNext();
                }
                else if (comparison > 0)
                {
                    var current = newEnumerator.Current;
                    changes.Add(new FileChange(
                        ChangeKind.New, current.Path, null, null,
                        0, current.Size, 0, current.Allocated, current.IsDirectory, current.Modified));
                    hasNew = newEnumerator.MoveNext();
                }
                else
                {
                    var old = oldEnumerator.Current;
                    var current = newEnumerator.Current;
                    if (old.Size == current.Size && old.Allocated == current.Allocated)
                    {
                        unchangedCount++;
                    }
                    else
                    {
                        var enlarged =
                            current.Allocated > old.Allocated ||
                            (current.Allocated == old.Allocated && current.Size > old.Size);
                        changes.Add(new FileChange(
                            enlarged ? ChangeKind.Enlarged : ChangeKind.Reduced,
                            current.Path, null, null,
                            old.Size, current.Size,
                            old.Allocated, current.Allocated,
                            current.IsDirectory, current.Modified));
                    }

                    hasOld = oldEnumerator.MoveNext();
                    hasNew = newEnumerator.MoveNext();
                }
            }

            changes = DetectMoves(changes);

            var sorted = changes
                .OrderBy(change => change.Path, StringComparer.Ordinal)
                .ThenBy(change => change.Kind)
                .ThenBy(change => change.OldPath, StringComparer.Ordinal)
                .ToList();

            long sizeDelta = 0;
            long allocatedDelta = 0;
            foreach (var change in sorted)
            {
                sizeDelta += change.NewSize - change.OldSize;
                allocatedDelta += change.NewAllocated - change.OldAllocated;
            }

            return new DiffReport(
                sorted,
                sorted.Count(change => change.Kind == ChangeKind.New),
                sorted.Count(change => change.Kind == ChangeKind.Deleted),
                sorted.Count(change => change.Kind == ChangeKind.Enlarged),
                sorted.Count(change => change.Kind == ChangeKind.Reduced),
                sorted.Count(change => change.Kind == ChangeKind.Moved),
                unchangedCount,
                sizeDelta,
                allocatedDelta);
        }

        private static List<FileChange> DetectMoves(List<FileChange> changes)
        {
            var movable = changes.Count(change => change.Kind is ChangeKind.New or ChangeKind.Deleted);
            if (movable > MovePairingLimit)
            {
                return changes;
            }

            var deletedByKey = new Dictionary<MoveKey, Queue<FileChange>>();
            foreach (var change in changes)
            {
                if (change.Kind != ChangeKind.Deleted)
                {
                    continue;
                }

                var key = new MoveKey(
                    change.IsDirectory,
                    change.OldSize,
                    change.OldAllocated,
                    change.Modified?.Ticks ?? long.MinValue);
                if (!deletedByKey.TryGetValue(key, out var queue))
                {
                    queue = new Queue<FileChange>();
                    deletedByKey[key] = queue;
                }

                queue.Enqueue(change);
            }

            var consumed = new HashSet<FileChange>();
            var final = new List<FileChange>(changes.Count);
            foreach (var change in changes)
            {
                if (change.Kind == ChangeKind.Deleted && consumed.Contains(change))
                {
                    continue;
                }

                if (change.Kind == ChangeKind.New)
                {
                    var key = new MoveKey(
                        change.IsDirectory,
                        change.NewSize,
                        change.NewAllocated,
                        change.Modified?.Ticks ?? long.MinValue);
                    if (deletedByKey.TryGetValue(key, out var queue) && queue.Count > 0)
                    {
                        var deleted = queue.Dequeue();
                        consumed.Add(deleted);
                        final.Add(change with
                        {
                            Kind = ChangeKind.Moved,
                            OldPath = deleted.Path,
                            NewPath = change.Path,
                            OldSize = deleted.OldSize,
                            OldAllocated = deleted.OldAllocated
                        });
                        continue;
                    }
                }

                final.Add(change);
            }

            return final;
        }

        private readonly record struct MoveKey(
            bool IsDirectory,
            long Size,
            long Allocated,
            long ModifiedTicks);
    }
}
