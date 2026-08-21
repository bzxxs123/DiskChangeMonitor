using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiskChangeMonitor.Import;
using DiskChangeMonitor.Models;
using DiskChangeMonitor.Storage;
using Xunit;

namespace DiskChangeMonitor.Tests.Import;

public class ImportCoordinatorTests : IAsyncLifetime
{
    private const string Header = "文件名称,大小,分配,修改时间,属性,文件,文件夹\r\n";

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "dcm-coordinator", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;
    private SqliteSnapshotStore _store = null!;
    private ImportCoordinator _coordinator = null!;

    public ImportCoordinatorTests()
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
                Thread.Sleep(50);
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

    private static MonitoredLocation Root => MonitoredLocation.FromPath("C:");

    private static string Row(string path, long size, long allocated, string date = "2024/1/2 15:04")
    {
        return $"\"{path}\",{size},{allocated},{date},----,,\r\n";
    }

    [Fact]
    public async Task SuccessfulImport_CommitsAndComparesWithPrevious()
    {
        var first = WriteCsv("first.csv", Header + Row(@"C:\a.txt", 100, 4096));
        var second = WriteCsv("second.csv", Header + Row(@"C:\a.txt", 150, 8192) + Row(@"C:\b.txt", 10, 4096));

        await _coordinator.ImportAsync(Root, first);
        var result = await _coordinator.ImportAsync(Root, second);

        Assert.Equal(SnapshotMetadata.Completed, result.Metadata.Status);
        Assert.Equal(2, result.Metadata.Rows);
        Assert.Equal(64, result.Metadata.Fingerprint.Length);
        Assert.Empty(result.Summary.Warnings);
        Assert.Equal(1, result.Comparison.NewCount);
        Assert.Equal(1, result.Comparison.EnlargedCount);
        Assert.Contains(result.Comparison.Changes, c => c.Kind == ChangeKind.Enlarged && c.Path == @"C:\a.txt");
        Assert.Contains(result.Comparison.Changes, c => c.Kind == ChangeKind.New && c.Path == @"C:\b.txt");
        Assert.Equal(2, (await _store.ListAsync("C:\\")).Count);
    }

    [Fact]
    public async Task FirstImport_HasEmptyComparison()
    {
        var csv = WriteCsv("only.csv", Header + Row(@"C:\a.txt", 100, 4096));

        var result = await _coordinator.ImportAsync(Root, csv);

        Assert.Empty(result.Comparison.Changes);
        Assert.Equal(1, result.Metadata.Rows);
        Assert.Equal(0, result.Summary.IgnoredRows);
    }

    [Fact]
    public async Task MissingColumns_RejectsWithoutChangingHistory()
    {
        var good = WriteCsv("good.csv", Header + Row(@"C:\a.txt", 100, 4096));
        var bad = WriteCsv("bad.csv", "文件名称,大小,修改时间,属性,文件,文件夹\r\nC:\\x,1,2024/1/2,----,,\r\n");

        await _coordinator.ImportAsync(Root, good);

        var exception = await Assert.ThrowsAsync<CsvHeaderException>(() => _coordinator.ImportAsync(Root, bad));

        Assert.Contains("分配", exception.Message);
        Assert.Single(await _store.ListAsync("C:\\"));
    }

    [Fact]
    public async Task MalformedRows_PropagateWarningsAndIgnoredRows()
    {
        var csv = WriteCsv(
            "malformed.csv",
            Header +
            Row(@"C:\ok1.txt", 100, 4096) +
            "\"C:\\bad.txt\",not-a-number,4096,2024/1/2 15:04,----,,\r\n" +
            Row(@"C:\ok2.txt", 200, 8192) +
            ",100,4096,2024/1/2 15:04,----,,\r\n");

        var result = await _coordinator.ImportAsync(Root, csv);

        Assert.Equal(2, result.Summary.Rows);
        Assert.Equal(2, result.Summary.IgnoredRows);
        Assert.Equal(2, result.Summary.Warnings.Count);
        Assert.Contains(result.Summary.Warnings, warning => warning.Contains("第 3 行"));
        Assert.Equal(2, result.Metadata.Rows);
        Assert.Equal(2, result.Metadata.IgnoredRows);
    }

    [Fact]
    public async Task Cancellation_CleansUpStaging_AndPreservesHistory()
    {
        var good = WriteCsv("good.csv", Header + Row(@"C:\keep.txt", 100, 4096));
        await _coordinator.ImportAsync(Root, good);

        var sb = new StringBuilder(Header);
        for (var i = 0; i < 25_000; i++)
        {
            sb.Append(Row($@"C:\f{i}.txt", i, 4096));
        }

        var big = WriteCsv("big.csv", sb.ToString());
        using var cts = new CancellationTokenSource();
        var progress = new Progress<ImportProgress>(p =>
        {
            if (p.Rows > 0)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _coordinator.ImportAsync(Root, big, progress, cts.Token));

        Assert.Single(await _store.ListAsync("C:\\"));
    }

    [Fact]
    public async Task LatestTwoSnapshots_AreTheComparisonPair()
    {
        var first = WriteCsv("1.csv", Header + Row(@"C:\a.txt", 100, 4096));
        var second = WriteCsv("2.csv", Header + Row(@"C:\a.txt", 100, 4096) + Row(@"C:\b.txt", 10, 4096));
        var third = WriteCsv("3.csv", Header + Row(@"C:\a.txt", 100, 4096) + Row(@"C:\b.txt", 20, 8192));

        await _coordinator.ImportAsync(Root, first);
        await _coordinator.ImportAsync(Root, second);
        var result = await _coordinator.ImportAsync(Root, third);

        Assert.Single(result.Comparison.Changes);
        var change = Assert.Single(result.Comparison.Changes);
        Assert.Equal(ChangeKind.Enlarged, change.Kind);
        Assert.Equal(@"C:\b.txt", change.Path);
    }

    [Fact]
    public async Task SourceCsv_IsNeverCopiedOrModified()
    {
        var csv = WriteCsv("source.csv", Header + Row(@"C:\a.txt", 100, 4096));
        var before = File.ReadAllBytes(csv);

        await _coordinator.ImportAsync(Root, csv);

        Assert.Equal(before, File.ReadAllBytes(csv));
    }

    [Fact]
    public async Task Fingerprint_DiffersForDifferentSources()
    {
        var csv1 = WriteCsv("f1.csv", Header + Row(@"C:\a.txt", 100, 4096));
        var csv2 = WriteCsv("f2.csv", Header + Row(@"C:\a.txt", 101, 4096));

        var r1 = await _coordinator.ImportAsync(Root, csv1);
        var r2 = await _coordinator.ImportAsync(Root, csv2);

        Assert.NotEqual(r1.Metadata.Fingerprint, r2.Metadata.Fingerprint);
    }

    [Fact]
    public void EstimateDatabaseBytes_IsProportional()
    {
        Assert.Equal(0, ImportCoordinator.EstimateDatabaseBytes(0));
        Assert.Equal(200, ImportCoordinator.EstimateDatabaseBytes(100));
    }
}
