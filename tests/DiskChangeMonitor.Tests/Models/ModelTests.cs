using System;
using DiskChangeMonitor.Models;
using Xunit;

namespace DiskChangeMonitor.Tests.Models;

public class ModelTests
{
    [Fact]
    public void MonitoredLocation_FromPath_NormalizesDriveRoot()
    {
        var location = MonitoredLocation.FromPath("c:");

        Assert.Equal("C:\\", location.Id);
        Assert.Equal("C:\\", location.RootPath);
        Assert.Equal("C:\\", location.DisplayName);
    }

    [Fact]
    public void MonitoredLocation_FromPath_NormalizesFolderPath()
    {
        var location = MonitoredLocation.FromPath(@"D:\Data\");

        Assert.Equal(@"D:\Data\", location.RootPath);
    }

    [Fact]
    public void MonitoredLocation_FromPath_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => MonitoredLocation.FromPath("   "));
    }

    [Fact]
    public void FileEntry_RoundTripsValues_WithNullModified()
    {
        var entry = new FileEntry(@"C:\file.txt", 100, 4096, null, "----", false);

        Assert.Equal(@"C:\file.txt", entry.Path);
        Assert.Equal(100, entry.Size);
        Assert.Equal(4096, entry.Allocated);
        Assert.Null(entry.Modified);
        Assert.False(entry.IsDirectory);
    }

    [Fact]
    public void FileEntry_ItemKindInferenceInputs_CarriesDirectoryFlag()
    {
        var directory = new FileEntry(@"C:\folder", 0, 0, null, "-D-", true);
        var file = new FileEntry(@"C:\folder\a.txt", 10, 4096, null, "----", false);

        Assert.True(directory.IsDirectory);
        Assert.False(file.IsDirectory);
    }

    [Fact]
    public void SnapshotMetadata_ExposesBothStatusConstants()
    {
        var metadata = new SnapshotMetadata("id", "C:\\", new DateTime(2026, 8, 21, 9, 30, 0), "x.csv", 10, "hash", 5, 1, SnapshotMetadata.Completed);

        Assert.Equal("Completed", metadata.Status);
        Assert.Equal("2026-08-21 09:30:00", metadata.DisplayTime);
        Assert.Equal("5 行", metadata.DisplayRows);
        Assert.Equal(SnapshotMetadata.Staging, SnapshotMetadata.Staging);
    }

    [Fact]
    public void ImportSummary_And_Progress_RoundTrip()
    {
        var summary = new ImportSummary(10, 2, new[] { "row 3 skipped" });
        var progress = new ImportProgress(100, 1000, 5, "解析导入");

        Assert.Equal(10, summary.Rows);
        Assert.Equal(2, summary.IgnoredRows);
        Assert.Single(summary.Warnings);
        Assert.Equal(100, progress.BytesRead);
        Assert.Equal("解析导入", progress.Stage);
    }

    [Fact]
    public void SnapshotData_RowFactory_ReturnsRows()
    {
        var rows = new[] { new FileEntry("a", 1, 4096, null, "", false) };
        var data = new SnapshotData("s1", "C:\\", () => rows);

        Assert.Equal("s1", data.SnapshotId);
        Assert.Same(rows, data.RowFactory());
    }

    [Fact]
    public void FileChange_KindText_MatchesChineseLabels()
    {
        Assert.Equal("新增", new FileChange(ChangeKind.New, "a", null, null, 0, 1, 0, 4096, false).KindText);
        Assert.Equal("删除", new FileChange(ChangeKind.Deleted, "a", null, null, 1, 0, 4096, 0, false).KindText);
        Assert.Equal("变大", new FileChange(ChangeKind.Enlarged, "a", null, null, 1, 2, 4096, 8192, false).KindText);
        Assert.Equal("变小", new FileChange(ChangeKind.Reduced, "a", null, null, 2, 1, 8192, 4096, false).KindText);
        Assert.Equal("移动", new FileChange(ChangeKind.Moved, "b", "a", "b", 1, 1, 4096, 4096, false).KindText);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "+1023 B")]
    [InlineData(1024, "+1.00 KB")]
    [InlineData(1536, "+1.50 KB")]
    [InlineData(-1536, "-1.50 KB")]
    [InlineData(1048576, "+1.00 MB")]
    [InlineData(1073741824, "+1.00 GB")]
    [InlineData(1099511627776, "+1.00 TB")]
    public void FileChange_FormatBytes_IsSignedAndInvariant(long value, string expected)
    {
        Assert.Equal(expected, FileChange.FormatBytes(value));
    }

    [Fact]
    public void DiffReport_RoundTripsValues()
    {
        var report = new DiffReport(
            new[] { new FileChange(ChangeKind.New, "a", null, null, 0, 1, 0, 4096, false) },
            1, 0, 0, 0, 0, 100, 1, 4096);

        Assert.Single(report.Changes);
        Assert.Equal(1, report.NewCount);
        Assert.Equal(100, report.UnchangedCount);
        Assert.Equal(1, report.SizeDelta);
        Assert.Equal(4096, report.AllocatedDelta);
    }
}
