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
using DiskChangeMonitor.ViewModels;
using Xunit;

namespace DiskChangeMonitor.Tests.ViewModels;

public class MainViewModelTests : IAsyncLifetime
{
    private const string Header = "文件名称,大小,分配,修改时间,属性,文件,文件夹\r\n";

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "dcm-viewmodel", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;
    private SqliteSnapshotStore _store = null!;

    public MainViewModelTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        _databasePath = Path.Combine(_tempDirectory, "snapshots.db");
    }

    public async Task InitializeAsync()
    {
        _store = new SqliteSnapshotStore(_databasePath);
        await _store.InitializeAsync();
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

    private sealed class FakeDialog : IFileDialogService
    {
        public string? CsvPath { get; set; }
        public string? SavePath { get; set; }

        public string? PickCsvFile() => CsvPath;

        public string? PickSaveFile(string defaultFileName) => SavePath;
    }

    private async Task<MainViewModel> CreateViewModelWithHistory()
    {
        var coordinator = new ImportCoordinator(_store);
        await coordinator.ImportAsync(MonitoredLocation.FromPath("C:"), WriteCsv("1.csv", Header + Row(@"C:\a.txt", 100, 4096)));
        await coordinator.ImportAsync(MonitoredLocation.FromPath("C:"), WriteCsv("2.csv", Header + Row(@"C:\a.txt", 150, 8192) + Row(@"C:\b.txt", 10, 4096)));

        var viewModel = new MainViewModel(_store, new FakeDialog());
        await viewModel.InitializeAsync();
        return viewModel;
    }

    [Fact]
    public async Task Initialize_LoadsRootsAndHistory()
    {
        var viewModel = await CreateViewModelWithHistory();

        Assert.Single(viewModel.Roots);
        Assert.Equal(2, viewModel.History.Count);
    }

    [Fact]
    public async Task ImportCommand_UpdatesHistoryAndOverview()
    {
        var viewModel = new MainViewModel(_store, new FakeDialog());
        await viewModel.InitializeAsync();
        viewModel.CsvPath = WriteCsv("3.csv", Header + Row(@"C:\a.txt", 200, 12288));

        await ((RelayCommand)viewModel.ImportCommand).ExecuteAsync();

        Assert.True(viewModel.History.Count == 1, "StatusText=" + viewModel.StatusText + " Warnings=" + viewModel.WarningsText);
        Assert.Contains("导入完成", viewModel.StatusText);
        Assert.Contains("逻辑大小变化", viewModel.OverviewText);
        Assert.Contains("新增", viewModel.CountsText);
    }

    [Fact]
    public async Task ImportCommand_RejectsMissingCsv()
    {
        var viewModel = new MainViewModel(_store, new FakeDialog());
        await viewModel.InitializeAsync();
        viewModel.CsvPath = Path.Combine(_tempDirectory, "missing.csv");

        await ((RelayCommand)viewModel.ImportCommand).ExecuteAsync();

        Assert.Contains("请先选择", viewModel.StatusText);
    }

    [Fact]
    public async Task Filters_AndSearch_ApplyToFilesAndDirectories()
    {
        var coordinator = new ImportCoordinator(_store);
        await coordinator.ImportAsync(MonitoredLocation.FromPath("C:"), WriteCsv("1.csv", Header + Row(@"C:\a.txt", 100, 4096)));
        await coordinator.ImportAsync(MonitoredLocation.FromPath("C:"), WriteCsv("2.csv", Header + Row(@"C:\a.txt", 150, 8192) + Row(@"C:\b.txt", 10, 4096)));

        var viewModel = new MainViewModel(_store, new FakeDialog());
        await viewModel.InitializeAsync();
        viewModel.CsvPath = WriteCsv("3.csv", Header + Row(@"C:\a.txt", 200, 12288) + Row(@"C:\c.txt", 1, 4096));
        await ((RelayCommand)viewModel.ImportCommand).ExecuteAsync();

        viewModel.FilterIndex = (int)ChangeKindFilter.New;

        Assert.All(viewModel.Files, item => Assert.Equal("新增", item.KindText));
        Assert.Single(viewModel.Files);

        viewModel.FilterIndex = (int)ChangeKindFilter.All;
        viewModel.SearchText = "c.txt";

        var file = Assert.Single(viewModel.Files);
        Assert.Equal(@"C:\c.txt", file.Path);
        Assert.Single(viewModel.Directories);
    }

    [Fact]
    public async Task ExportCommand_WritesCsvReport()
    {
        var exportPath = Path.Combine(_tempDirectory, "report.csv");
        var csvPath = WriteCsv("3.csv", Header + Row(@"C:\a.txt", 200, 12288));
        var dialog = new FakeDialog { SavePath = exportPath };
        var viewModel = new MainViewModel(_store, dialog);
        await viewModel.InitializeAsync();
        viewModel.CsvPath = csvPath;
        await ((RelayCommand)viewModel.ImportCommand).ExecuteAsync();

        await ((RelayCommand)viewModel.ExportCommand).ExecuteAsync();

        Assert.True(File.Exists(exportPath));
        Assert.Contains("已导出", viewModel.StatusText);
        var content = File.ReadAllText(exportPath, new UTF8Encoding(true));
        Assert.Contains("变化类型,路径", content);
    }
}
