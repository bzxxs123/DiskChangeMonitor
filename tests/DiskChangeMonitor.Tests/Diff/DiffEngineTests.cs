using System;
using System.Collections.Generic;
using System.Linq;
using DiskChangeMonitor.Diff;
using DiskChangeMonitor.Models;
using Xunit;

namespace DiskChangeMonitor.Tests.Diff;

public class DiffEngineTests
{
    private static SnapshotData Snapshot(params FileEntry[] rows)
    {
        var sorted = rows
            .OrderBy(row => row.Path, StringComparer.Ordinal)
            .ToList();
        return new SnapshotData("s", "C:\\", () => sorted);
    }

    private static FileEntry File(string path, long size = 100, long allocated = 4096, DateTime? modified = null)
    {
        return new FileEntry(path, size, allocated, modified, "----", false);
    }

    [Fact]
    public void Classifies_NewDeletedEnlargedReducedUnchanged()
    {
        var older = Snapshot(
            File(@"C:\a.txt", 100, 4096),
            File(@"C:\b.txt", 200, 8192),
            File(@"C:\gone.txt", 50, 4096),
            File(@"C:\shrink.txt", 300, 12288));
        var newer = Snapshot(
            File(@"C:\a.txt", 150, 8192),
            File(@"C:\b.txt", 180, 4096),
            File(@"C:\c.txt", 10, 4096),
            File(@"C:\shrink.txt", 300, 8192));

        var report = DiffEngine.Compare(older, newer);

        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.Enlarged && c.Path == @"C:\a.txt" && c.SizeDelta == 50 && c.AllocatedDelta == 4096);
        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.Reduced && c.Path == @"C:\b.txt" && c.SizeDelta == -20);
        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.Reduced && c.Path == @"C:\shrink.txt" && c.AllocatedDelta == -4096);
        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.Deleted && c.Path == @"C:\gone.txt");
        Assert.Contains(report.Changes, c => c.Kind == ChangeKind.New && c.Path == @"C:\c.txt");
        Assert.Equal(1, report.EnlargedCount);
        Assert.Equal(2, report.ReducedCount);
        Assert.Equal(1, report.DeletedCount);
        Assert.Equal(1, report.NewCount);
        Assert.Equal(0, report.MovedCount);
        Assert.Equal(-10, report.SizeDelta);
        Assert.Equal(-4096, report.AllocatedDelta);
    }

    [Fact]
    public void SamePath_SameSize_CountsAsUnchanged()
    {
        var older = Snapshot(File(@"C:\a.txt", 100, 4096));
        var newer = Snapshot(File(@"C:\a.txt", 100, 4096));

        var report = DiffEngine.Compare(older, newer);

        Assert.Empty(report.Changes);
        Assert.Equal(1, report.UnchangedCount);
        Assert.Equal(0, report.SizeDelta);
    }

    [Fact]
    public void AllocatedGrowth_WithSameSize_IsEnlarged()
    {
        var older = Snapshot(File(@"C:\a.txt", 100, 4096));
        var newer = Snapshot(File(@"C:\a.txt", 100, 8192));

        var report = DiffEngine.Compare(older, newer);

        var change = Assert.Single(report.Changes);
        Assert.Equal(ChangeKind.Enlarged, change.Kind);
        Assert.Equal(4096, change.AllocatedDelta);
    }

    [Fact]
    public void Detects_MovesByMetadata_PairsOldAndNewPaths()
    {
        var modified = new DateTime(2026, 8, 1, 12, 0, 0);
        var older = Snapshot(
            File(@"C:\old\folder\a.txt", 100, 4096, modified),
            File(@"C:\old\folder\b.txt", 200, 8192, modified));
        var newer = Snapshot(
            File(@"C:\new\folder\a.txt", 100, 4096, modified),
            File(@"C:\old\folder\b.txt", 200, 8192, modified));

        var report = DiffEngine.Compare(older, newer);

        var moved = Assert.Single(report.Changes);
        Assert.Equal(ChangeKind.Moved, moved.Kind);
        Assert.Equal(@"C:\old\folder\a.txt", moved.OldPath);
        Assert.Equal(@"C:\new\folder\a.txt", moved.NewPath);
        Assert.Equal(1, report.MovedCount);
        Assert.Equal(0, report.NewCount);
        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(1, report.UnchangedCount);
    }

    [Fact]
    public void MovePairingLimit_SkipsPairingWhenTooManyRows()
    {
        var previous = DiffEngine.MovePairingLimit;
        try
        {
            DiffEngine.MovePairingLimit = 2;

            var modified = new DateTime(2026, 8, 1, 12, 0, 0);
            var older = Snapshot(
                File(@"C:\old1.txt", 100, 4096, modified),
                File(@"C:\old2.txt", 100, 4096, modified),
                File(@"C:\old3.txt", 100, 4096, modified));
            var newer = Snapshot(
                File(@"C:\new1.txt", 100, 4096, modified),
                File(@"C:\new2.txt", 100, 4096, modified),
                File(@"C:\new3.txt", 100, 4096, modified));

            var report = DiffEngine.Compare(older, newer);

            Assert.Equal(0, report.MovedCount);
            Assert.Equal(3, report.NewCount);
            Assert.Equal(3, report.DeletedCount);
        }
        finally
        {
            DiffEngine.MovePairingLimit = previous;
        }
    }

    [Fact]
    public void Changes_AreSortedDeterministically()
    {
        var older = Snapshot(File(@"C:\z.txt"), File(@"C:\a.txt"));
        var newer = Snapshot(File(@"C:\z.txt"), File(@"C:\m.txt"));

        var report = DiffEngine.Compare(older, newer);

        var paths = report.Changes.Select(c => c.Path).ToList();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.Ordinal), paths);
    }

    [Fact]
    public void EmptySnapshots_ProduceEmptyReport()
    {
        var report = DiffEngine.Compare(Snapshot(), Snapshot());

        Assert.Empty(report.Changes);
        Assert.Equal(0, report.AllocatedDelta);
        Assert.Equal(0, report.UnchangedCount);
    }

    [Fact]
    public void DirectoryAggregator_GroupsByDirectory_AndSumsDeltas()
    {
        var changes = new[]
        {
            new FileChange(ChangeKind.Enlarged, @"C:\a\f1.txt", null, null, 10, 20, 4096, 8192, false),
            new FileChange(ChangeKind.New, @"C:\a\f2.txt", null, null, 0, 5, 0, 4096, false),
            new FileChange(ChangeKind.Deleted, @"C:\b\gone.txt", null, null, 5, 0, 4096, 0, false),
            new FileChange(ChangeKind.Moved, @"C:\c\m.txt", @"C:\b\m.txt", @"C:\c\m.txt", 1, 1, 4096, 4096, false)
        };

        var aggregate = DirectoryAggregator.Aggregate(changes);

        var byPath = aggregate.ToDictionary(a => a.Path);
        Assert.Equal(15, byPath[@"C:\a"].SizeDelta);
        Assert.Equal(8192, byPath[@"C:\a"].AllocatedDelta);
        Assert.Equal(2, byPath[@"C:\a"].Count);
        Assert.Equal(-5, byPath[@"C:\b"].SizeDelta);
        Assert.Equal(-4096, byPath[@"C:\b"].AllocatedDelta);
        Assert.Equal(1, byPath[@"C:\b"].Count);
        Assert.Equal(1, byPath[@"C:\c"].Count);
    }
}
