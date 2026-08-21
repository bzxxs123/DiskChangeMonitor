using System;
using System.IO;
using System.Linq;
using System.Text;
using DiskChangeMonitor.Export;
using DiskChangeMonitor.Models;
using Xunit;

namespace DiskChangeMonitor.Tests.Export;

public class CsvExporterTests
{
    private static DiffReport Report()
    {
        return new DiffReport(
            new[]
            {
                new FileChange(ChangeKind.Moved, @"C:\new\a.txt", @"C:\old\a.txt", @"C:\new\a.txt", 100, 100, 4096, 4096, false),
                new FileChange(ChangeKind.New, @"C:\带,逗号\新文件.txt", null, null, 0, 50, 0, 4096, false),
                new FileChange(ChangeKind.Enlarged, @"C:\quote""file.txt", null, null, 10, 20, 4096, 8192, false)
            },
            1, 0, 1, 0, 1, 10, 60, 8192);
    }

    [Fact]
    public void Export_QuotesCommasQuotesAndWritesUtf8()
    {
        using var buffer = new MemoryStream();
        using (var writer = new StreamWriter(buffer, new UTF8Encoding(true), leaveOpen: true))
        {
            CsvExporter.Export(writer, Report());
        }

        var text = new UTF8Encoding(true).GetString(buffer.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("变化类型,路径,变化前路径,变化后路径", lines[0]);
        Assert.Contains("\"C:\\带,逗号\\新文件.txt\"", lines[2]);
        Assert.Contains("\"C:\\quote\"\"file.txt\"", lines[3]);
    }

    [Fact]
    public void Export_ToFile_WritesStableColumnsAndAllRows()
    {
        var path = Path.Combine(Path.GetTempPath(), "dcm-export-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            CsvExporter.Export(path, Report());

            var lines = File.ReadAllLines(path, new UTF8Encoding(true));
            Assert.Equal(4, lines.Length);
            Assert.Equal(11, lines[0].Split(',').Length);
            Assert.Equal("移动", lines[1].Split(',')[0]);
            Assert.Equal("新增", lines[2].Split(',')[0]);
            Assert.Equal("变大", lines[3].Split(',')[0]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Export_EmptyReport_WritesOnlyHeader()
    {
        using var writer = new StringWriter();

        CsvExporter.Export(writer, new DiffReport(Array.Empty<FileChange>(), 0, 0, 0, 0, 0, 0, 0, 0));

        var lines = writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }
}
