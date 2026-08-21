using System.IO;
using DiskChangeMonitor.Diff;
using DiskChangeMonitor.Models;

namespace DiskChangeMonitor.ViewModels
{
    public sealed class FileChangeItem
    {
        public FileChangeItem(FileChange change)
        {
            Change = change;
        }

        public FileChange Change { get; }
        public string KindText => Change.KindText;
        public string Path => Change.Path;
        public string OldPath => Change.OldPath ?? string.Empty;
        public string NewPath => Change.NewPath ?? Change.Path;
        public string SizeDeltaText => FileChange.FormatBytes(Change.SizeDelta);
        public string AllocatedDeltaText => FileChange.FormatBytes(Change.AllocatedDelta);
        public string OldSizeText => FileChange.FormatBytes(Change.OldSize);
        public string NewSizeText => FileChange.FormatBytes(Change.NewSize);
        public string Directory => string.IsNullOrEmpty(System.IO.Path.GetDirectoryName(Change.Path))
            ? Change.Path
            : System.IO.Path.GetDirectoryName(Change.Path)!;
    }

    public sealed class DirectoryChangeItem
    {
        public DirectoryChangeItem(DirectoryChange change)
        {
            Change = change;
        }

        public DirectoryChange Change { get; }
        public string Path => Change.Path;
        public string SizeDeltaText => FileChange.FormatBytes(Change.SizeDelta);
        public string AllocatedDeltaText => FileChange.FormatBytes(Change.AllocatedDelta);
        public long Count => Change.Count;
    }
}
