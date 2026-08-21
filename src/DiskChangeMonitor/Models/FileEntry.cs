using System;

namespace DiskChangeMonitor.Models
{
    /// <summary>One file or folder row imported from a WizTree CSV export.</summary>
    public sealed record FileEntry(
        string Path,
        long Size,
        long Allocated,
        DateTime? Modified,
        string Attributes,
        bool IsDirectory,
        long? FileCount = null,
        long? FolderCount = null);
}
