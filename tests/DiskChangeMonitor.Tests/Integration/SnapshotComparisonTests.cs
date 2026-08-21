using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DiskChangeMonitor.Diff;
using DiskChangeMonitor.Import;
using DiskChangeMonitor.Models;
using DiskChangeMonitor.Storage;
using Xunit;

namespace DiskChangeMonitor.Tests.Integration;

public class SnapshotComparisonTests : IAsyncLifetime
{
    private const string Header = "文件名称,大小,分配,修改时间,属性,文件,文件夹\r\n";

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "dcm-integration", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;
    private SqliteSnapshotStore _store = null!;
    private ImportCoordinator _coordinator = null!;

    public SnapshotComparisonTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        _databasePath = Path.Combine(_tempDirectory, "snapshots.db");
    }

    public async Task InitializeAsync()
    {
        _store = new SqliteSnapshotStore(_databasePath);
        await _store.InitializeAsync();
        _coordinator = new ImportCoordinator(_store);
    }

    public Task DisposeAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }

                return Task.CompletedTask;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        return Task.CompletedTask;
    }

    private string WriteCsv(string fileName, string content)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(true));
        return path;
    }

    private static string Row(string path, long size, long allocated)
    {
        return $"\"{path}\",{size},{allocated},2024/1/2 15:04,----,,\r\n";
    }

    private string GenerateCsv(string fileName, int count, Func<int, string> rowBuilder)
    {
        var builder = new StringBuilder(Header);
        for (var i = 0; i < count; i++)
        {
            builder.Append(rowBuilder(i));
        }

        return WriteCsv(fileName, builder.ToString());
    }

    [Fact]
    public async Task TwoExports_SavedReloadedCompared_WithExpectedDeltas()
    {
        var first = GenerateCsv("first.csv", 500, i => Row($@"C:\data\f{i:D4}.bin", 100 + i, 4096 + i * 4));
        var second = GenerateCsv("second.csv", 500, i =>
            i == 250
                ? Row($@"C:\data\f{i:D4}.bin", 999, 99999)
                : i == 499
                    ? Row($@"C:\data\new.bin", 1, 4096)
                    : Row($@"C:\data\f{i:D4}.bin", 100 + i, 4096 + i * 4));

        await _coordinator.ImportAsync(MonitoredLocation.FromPath("C:"), first);
        await _coordinator.ImportAsync(MonitoredLocation.FromPath("C:"), second);

        // Reload from disk with a fresh store instance, like a new app session.
        var reloadedStore = new SqliteSnapshotStore(_databasePath);
        await reloadedStore.InitializeAsync();
        var history = await reloadedStore.ListAsync("C:\\");
        Assert.Equal(2, history.Count);

        var comparison = DiffEngine.Compare(
            await reloadedStore.LoadAsync(history[1].Id),
            await reloadedStore.LoadAsync(history[0].Id));

        Assert.Equal(1, comparison.EnlargedCount);
        Assert.Equal(1, comparison.NewCount);
        Assert.Equal(1, comparison.DeletedCount);
        Assert.Equal(498, comparison.UnchangedCount);
        Assert.Equal(51, comparison.SizeDelta);
        Assert.Contains(comparison.Changes, c => c.Kind == ChangeKind.Enlarged && c.Path == @"C:\data\f0250.bin");
        Assert.Contains(comparison.Changes, c => c.Kind == ChangeKind.New && c.Path == @"C:\data\new.bin");
        Assert.Contains(comparison.Changes, c => c.Kind == ChangeKind.Deleted && c.Path == @"C:\data\f0499.bin");
    }

    [Fact]
    public async Task SevenExports_PruneToFive_CompareLatestTwo()
    {
        string? lastResultFingerprint = null;
        for (var i = 1; i <= 7; i++)
        {
            var csv = GenerateCsv($"e{i}.csv", 10, n => Row($@"C:\f{n:D3}.txt", 100 + i, 4096 + i * 2));
            var result = await _coordinator.ImportAsync(MonitoredLocation.FromPath("C:"), csv);
            lastResultFingerprint = result.Metadata.Fingerprint;
        }

        var history = await _store.ListAsync("C:\\");

        Assert.Equal(5, history.Count);
        Assert.Equal(lastResultFingerprint, history[0].Fingerprint);
        Assert.NotEqual(history[0].Id, history[1].Id);
    }

    [Fact]
    public async Task LargeExport_StreamsImportWithBoundedMemory()
    {
        var rowCount = 200_000;
        var csv = GenerateCsv("large.csv", rowCount, i => Row($@"C:\bulk\file{i:D6}.bin", i, i * 2));
        var progress = new List<ImportProgress>();

        var result = await _coordinator.ImportAsync(
            MonitoredLocation.FromPath("C:"),
            csv,
            new Progress<ImportProgress>(progress.Add));

        Assert.Equal(rowCount, result.Summary.Rows);
        Assert.Equal(0, result.Summary.IgnoredRows);
        Assert.NotEmpty(progress);
        Assert.Equal(new FileInfo(csv).Length, progress[^1].TotalBytes);

        var data = await _store.LoadAsync(result.Metadata.Id);
        Assert.Equal(rowCount, data.RowFactory().Count());
    }
}
