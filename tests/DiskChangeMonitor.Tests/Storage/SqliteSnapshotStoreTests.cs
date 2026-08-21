using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiskChangeMonitor.Models;
using DiskChangeMonitor.Storage;
using Xunit;

namespace DiskChangeMonitor.Tests.Storage;

public class SqliteSnapshotStoreTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        "dcm-tests",
        Guid.NewGuid().ToString("N") + ".db");

    private SqliteSnapshotStore _store = null!;

    public Task InitializeAsync()
    {
        _store = new SqliteSnapshotStore(_databasePath);
        return _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var path in new[] { _databasePath + "-wal", _databasePath + "-shm", _databasePath })
        {
            for (var attempt = 0; attempt < 5 && File.Exists(path); attempt++)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static MonitoredLocation Root => MonitoredLocation.FromPath("C:");

    private static IEnumerable<FileEntry> Rows(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new FileEntry($"C:\\f{i:D6}.bin", i, i * 2, null, "----", false);
        }
    }

    private async Task<SnapshotMetadata> ImportAsync(string sourcePath, DateTime importedAt, int rows = 10)
    {
        var staging = await _store.BeginImportAsync(Root, sourcePath, 100, "hash", importedAt);
        await _store.AppendRowsAsync(staging.Id, Rows(rows));
        return await _store.CommitAsync(staging.Id, rows, 0);
    }

    [Fact]
    public void Initialize_CreatesDatabaseFile()
    {
        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task BeginImport_Commit_RoundTripsMetadata()
    {
        var importedAt = new DateTime(2026, 8, 21, 10, 0, 0);
        var metadata = await ImportAsync(@"C:\export1.csv", importedAt, 12);

        Assert.Equal(SnapshotMetadata.Completed, metadata.Status);
        Assert.Equal(@"C:\export1.csv", metadata.SourcePath);
        Assert.Equal(100, metadata.SourceBytes);
        Assert.Equal("hash", metadata.Fingerprint);
        Assert.Equal(12, metadata.Rows);
        Assert.Equal(importedAt, metadata.ImportedAt);
        Assert.Equal("C:\\", metadata.MonitoredRoot);
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst_CompletedOnly()
    {
        var first = await ImportAsync("a.csv", new DateTime(2026, 8, 1, 10, 0, 0), 5);
        var second = await ImportAsync("b.csv", new DateTime(2026, 8, 2, 10, 0, 0), 7);

        var history = await _store.ListAsync("C:\\");

        Assert.Equal(new[] { second.Id, first.Id }, history.Select(h => h.Id));
        Assert.Equal(5, history[1].Rows);
        Assert.Equal(7, history[0].Rows);
    }

    [Fact]
    public async Task LoadAsync_StreamsRows_SortedByPathOrdinal()
    {
        var staging = await _store.BeginImportAsync(Root, "a.csv", 1, "h", new DateTime(2026, 8, 1));
        await _store.AppendRowsAsync(staging.Id, new[]
        {
            new FileEntry("C:\\b.txt", 2, 4096, null, "", false),
            new FileEntry("C:\\a.txt", 1, 4096, null, "", false),
            new FileEntry("C:\\A.txt", 1, 4096, null, "", false)
        });
        await _store.CommitAsync(staging.Id, 3, 0);

        var data = await _store.LoadAsync(staging.Id);
        var loaded = data.RowFactory().ToList();

        Assert.Equal(new[] { "C:\\A.txt", "C:\\a.txt", "C:\\b.txt" }, loaded.Select(e => e.Path));
        Assert.Equal(2, loaded[2].Size);
    }

    [Fact]
    public async Task Commit_PrunesHistoryToFiveNewest()
    {
        var imported = new List<string>();
        for (var i = 1; i <= 7; i++)
        {
            var metadata = await ImportAsync($"e{i}.csv", new DateTime(2026, 8, i, 10, 0, 0), 3);
            imported.Add(metadata.Id);
        }

        var history = await _store.ListAsync("C:\\");

        Assert.Equal(5, history.Count);
        Assert.Equal(imported[6], history[0].Id);
        Assert.Equal(imported[5], history[1].Id);
        Assert.Equal(imported[4], history[2].Id);
        Assert.Equal(imported[3], history[3].Id);
        Assert.Equal(imported[2], history[4].Id);
        Assert.DoesNotContain(imported[0], history.Select(h => h.Id));
        Assert.DoesNotContain(imported[1], history.Select(h => h.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.LoadAsync(imported[0]));
    }

    [Fact]
    public async Task CancelledImport_LeavesHistoryUnchanged()
    {
        var existing = await ImportAsync("keep.csv", new DateTime(2026, 8, 1, 10, 0, 0), 4);

        var staging = await _store.BeginImportAsync(Root, "bad.csv", 100, "h2", new DateTime(2026, 8, 2, 10, 0, 0));
        await _store.AppendRowsAsync(staging.Id, Rows(20));
        await _store.CancelAsync(staging.Id);

        var history = await _store.ListAsync("C:\\");

        Assert.Single(history);
        Assert.Equal(existing.Id, history[0].Id);
        Assert.Throws<InvalidOperationException>(() => _store.LoadAsync(staging.Id).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task StagingSnapshot_IsInvisibleUntilCommit()
    {
        var staging = await _store.BeginImportAsync(Root, "a.csv", 1, "h", new DateTime(2026, 8, 1));

        var history = await _store.ListAsync("C:\\");

        Assert.Empty(history);
    }

    [Fact]
    public async Task LargeAppend_BatchesAllRows()
    {
        var staging = await _store.BeginImportAsync(Root, "big.csv", 1, "h", new DateTime(2026, 8, 1));
        var count = 10_000;

        await _store.AppendRowsAsync(staging.Id, Rows(count));
        await _store.CommitAsync(staging.Id, count, 0);

        var data = await _store.LoadAsync(staging.Id);
        Assert.Equal(count, data.RowFactory().Count());
    }
}
