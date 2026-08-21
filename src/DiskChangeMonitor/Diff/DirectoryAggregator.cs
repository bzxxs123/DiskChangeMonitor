using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiskChangeMonitor.Models;

namespace DiskChangeMonitor.Diff
{
    public sealed record DirectoryChange(string Path, long SizeDelta, long AllocatedDelta, long Count);

    /// <summary>
    /// Aggregates item-level changes by their containing directory. Pure presentation
    /// helper; the diff engine never materializes directory rows.
    /// </summary>
    public static class DirectoryAggregator
    {
        public static IReadOnlyList<DirectoryChange> Aggregate(IEnumerable<FileChange> changes)
        {
            var aggregates = new SortedDictionary<string, AggregateEntry>(StringComparer.Ordinal);
            foreach (var change in changes)
            {
                var directory = Path.GetDirectoryName(change.Path);
                if (string.IsNullOrEmpty(directory))
                {
                    directory = change.Path;
                }

                if (!aggregates.TryGetValue(directory, out var current))
                {
                    current = default;
                }

                aggregates[directory] = new AggregateEntry(
                    current.SizeDelta + change.NewSize - change.OldSize,
                    current.AllocatedDelta + change.NewAllocated - change.OldAllocated,
                    current.Count + 1);
            }

            return aggregates
                .Select(pair => new DirectoryChange(pair.Key, pair.Value.SizeDelta, pair.Value.AllocatedDelta, pair.Value.Count))
                .ToList();
        }

        private readonly record struct AggregateEntry(long SizeDelta, long AllocatedDelta, long Count);
    }
}
