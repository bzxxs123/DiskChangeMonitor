using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DiskChangeMonitor.Diff;
using DiskChangeMonitor.Export;
using DiskChangeMonitor.Import;
using DiskChangeMonitor.Models;
using DiskChangeMonitor.Storage;

namespace DiskChangeMonitor.ViewModels
{
    public enum ChangeKindFilter
    {
        All = 0,
        New = 1,
        Deleted = 2,
        Enlarged = 3,
        Reduced = 4,
        Moved = 5
    }

    public sealed class MainViewModel : ViewModelBase
    {
        private readonly ISnapshotStore _store;
        private readonly IFileDialogService _dialogs;
        private readonly Progress<ImportProgress> _progress;

        private string _rootPathText = "C:\\";
        private string _csvPath = string.Empty;
        private string _statusText = "就绪";
        private string _warningsText = string.Empty;
        private double _progressValue;
        private bool _isBusy;
        private DiffReport? _report;
        private ChangeKindFilter _filterKind = ChangeKindFilter.All;
        private string _searchText = string.Empty;

        public MainViewModel(ISnapshotStore store, IFileDialogService dialogs)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _progress = new Progress<ImportProgress>(UpdateProgress);

            BrowseCommand = new RelayCommand(BrowseAsync);
            ImportCommand = new RelayCommand(ImportAsync, () => !IsBusy);
            ExportCommand = new RelayCommand(ExportAsync, () => !IsBusy && _report != null);
        }

        public ObservableCollection<MonitoredLocation> Roots { get; } = new();

        public ObservableCollection<SnapshotMetadata> History { get; } = new();

        public ObservableCollection<DirectoryChangeItem> Directories { get; } = new();

        public ObservableCollection<FileChangeItem> Files { get; } = new();

        public static string[] FilterOptions { get; } = { "全部", "新增", "删除", "变大", "变小", "移动" };

        public ICommand BrowseCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }

        public string RootPathText
        {
            get => _rootPathText;
            set => SetProperty(ref _rootPathText, value);
        }

        public string CsvPath
        {
            get => _csvPath;
            set => SetProperty(ref _csvPath, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string WarningsText
        {
            get => _warningsText;
            set => SetProperty(ref _warningsText, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseCommands();
                }
            }
        }

        public int FilterIndex
        {
            get => (int)_filterKind;
            set
            {
                if (SetProperty(ref _filterKind, (ChangeKindFilter)value))
                {
                    ApplyFilters();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilters();
                }
            }
        }

        public string OverviewText =>
            _report == null
                ? "暂无对比结果"
                : $"逻辑大小变化: {FileChange.FormatBytes(_report.SizeDelta)}    分配空间变化: {FileChange.FormatBytes(_report.AllocatedDelta)}";

        public string CountsText =>
            _report == null
                ? string.Empty
                : $"新增 {_report.NewCount:N0} · 删除 {_report.DeletedCount:N0} · 变大 {_report.EnlargedCount:N0} · 变小 {_report.ReducedCount:N0} · 移动 {_report.MovedCount:N0} · 未变化 {_report.UnchangedCount:N0}";

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            await RefreshRootsAsync(ct);
            await RefreshHistoryAsync(ct);
        }

        private async Task BrowseAsync()
        {
            var picked = _dialogs.PickCsvFile();
            if (picked != null)
            {
                CsvPath = picked;
            }

            await Task.CompletedTask;
        }

        private async Task ImportAsync()
        {
            if (IsBusy)
            {
                return;
            }

            MonitoredLocation root;
            try
            {
                root = MonitoredLocation.FromPath(RootPathText);
            }
            catch (ArgumentException ex)
            {
                StatusText = ex.Message;
                return;
            }

            if (!File.Exists(CsvPath))
            {
                StatusText = "请先选择一个 WizTree CSV 导出文件。";
                return;
            }

            IsBusy = true;
            ProgressValue = 0;
            WarningsText = string.Empty;
            try
            {
                var sourceBytes = new FileInfo(CsvPath).Length;
                StatusText = $"预计数据库占用约 {FileChange.FormatBytes(ImportCoordinator.EstimateDatabaseBytes(sourceBytes))}，开始导入…";
                var result = await new ImportCoordinator(_store).ImportAsync(root, CsvPath, _progress);

                _report = result.Comparison;
                OnPropertyChanged(nameof(OverviewText));
                OnPropertyChanged(nameof(CountsText));
                ApplyFilters();

                WarningsText = result.Summary.Warnings.Count == 0
                    ? "无"
                    : string.Join(Environment.NewLine, result.Summary.Warnings);
                StatusText =
                    $"导入完成: {result.Summary.Rows:N0} 行，忽略 {result.Summary.IgnoredRows:N0} 行，共 {result.Metadata.SourceBytes:N0} 字节";
                await RefreshRootsAsync();
                await RefreshHistoryAsync();
            }
            catch (OperationCanceledException)
            {
                StatusText = "导入已取消。";
            }
            catch (Exception ex)
            {
                StatusText = "导入失败: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
            }
        }

        private async Task ExportAsync()
        {
            if (_report == null)
            {
                return;
            }

            var path = _dialogs.PickSaveFile($"磁盘变化报告-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            if (path == null)
            {
                return;
            }

            try
            {
                CsvExporter.Export(path, _report);
                StatusText = "已导出: " + path;
            }
            catch (Exception ex)
            {
                StatusText = "导出失败: " + ex.Message;
            }

            await Task.CompletedTask;
        }

        private void UpdateProgress(ImportProgress progress)
        {
            ProgressValue = progress.TotalBytes > 0 ? Math.Min(100, progress.BytesRead * 100.0 / progress.TotalBytes) : 0;
            StatusText = $"{progress.Stage}… ({progress.Rows:N0} 行 / {FileChange.FormatBytes(progress.BytesRead)})";
        }

        private void ApplyFilters()
        {
            Directories.Clear();
            Files.Clear();
            if (_report == null)
            {
                return;
            }

            var filtered = _report.Changes
                .Where(change => MatchesFilter(change))
                .ToList();

            foreach (var directory in DirectoryAggregator.Aggregate(filtered))
            {
                Directories.Add(new DirectoryChangeItem(directory));
            }

            foreach (var change in filtered)
            {
                Files.Add(new FileChangeItem(change));
            }
        }

        private bool MatchesFilter(FileChange change)
        {
            var matchesKind = _filterKind switch
            {
                ChangeKindFilter.All => true,
                ChangeKindFilter.New => change.Kind == ChangeKind.New,
                ChangeKindFilter.Deleted => change.Kind == ChangeKind.Deleted,
                ChangeKindFilter.Enlarged => change.Kind == ChangeKind.Enlarged,
                ChangeKindFilter.Reduced => change.Kind == ChangeKind.Reduced,
                ChangeKindFilter.Moved => change.Kind == ChangeKind.Moved,
                _ => true
            };

            if (!matchesKind)
            {
                return false;
            }

            return _searchText.Length == 0 ||
                   change.Path.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task RefreshRootsAsync(CancellationToken ct = default)
        {
            Roots.Clear();
            foreach (var rootPath in await _store.ListRootsAsync(ct))
            {
                Roots.Add(MonitoredLocation.FromPath(rootPath));
            }

            if (Roots.Count == 0)
            {
                Roots.Add(MonitoredLocation.FromPath("C:"));
            }
        }

        private async Task RefreshHistoryAsync(CancellationToken ct = default)
        {
            History.Clear();
            foreach (var snapshot in await _store.ListAsync(MonitoredLocation.NormalizeRoot(RootPathText), ct))
            {
                History.Add(snapshot);
            }
        }

        private void RaiseCommands()
        {
            ((RelayCommand)BrowseCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ImportCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ExportCommand).RaiseCanExecuteChanged();
        }
    }
}
