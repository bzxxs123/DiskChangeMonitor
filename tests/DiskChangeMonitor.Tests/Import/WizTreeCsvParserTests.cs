using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DiskChangeMonitor.Import;
using DiskChangeMonitor.Models;
using Xunit;

namespace DiskChangeMonitor.Tests.Import;

public class WizTreeCsvParserTests
{
    private const string Header = "文件名称,大小,分配,修改时间,属性,文件,文件夹\r\n";

    private static WizTreeCsvParser Parse(string csv)
    {
        var bytes = new UTF8Encoding(true).GetBytes(csv);
        return new WizTreeCsvParser(new MemoryStream(bytes));
    }

    private static List<ParsedRow> ParseAll(string csv)
    {
        return Parse(csv).ReadDataRows().ToList();
    }

    [Fact]
    public void Parses_BomCsvCrLf_QuotedChinesePathsWithCommasAndQuotes()
    {
        var csv = Header +
                  "\"C:\\中文目录\\文件, 带逗号.txt\",1024,4096,2024/1/2 15:04,----,,\r\n" +
                  "\"C:\\带\"\"引号\"\"的文件.txt\",2048,8192,2024/1/2 15:05,----,,\r\n" +
                  "C:\\普通文件.txt,3072,12288,2024/1/2 15:06,----,,\r\n";

        var rows = ParseAll(csv);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.NotNull(row.Entry));
        Assert.Equal(@"C:\中文目录\文件, 带逗号.txt", rows[0].Entry!.Path);
        Assert.Equal(1024, rows[0].Entry!.Size);
        Assert.Equal(@"C:\带""引号""的文件.txt", rows[1].Entry!.Path);
        Assert.Equal(3072, rows[2].Entry!.Size);
    }

    [Fact]
    public void Parses_ScientificNotationSizes()
    {
        var csv = Header + "C:\\big.bin,1.23456E+07,1.5E+07,2024/1/2 15:04,----,,\r\n";

        var row = Assert.Single(ParseAll(csv));

        Assert.Equal(12345600, row.Entry!.Size);
        Assert.Equal(15000000, row.Entry.Allocated);
    }

    [Fact]
    public void Infers_Directories_FromSummaryColumnsOrAttributes()
    {
        var csv = Header +
                  "C:\\folder1,0,0,2024/1/2 15:04,----,5,2\r\n" +
                  "C:\\folder2,,,2024/1/2 15:04,D,,\r\n" +
                  "C:\\folder2\\a.txt,100,4096,2024/1/2 15:05,----,,\r\n";

        var rows = ParseAll(csv);

        Assert.True(rows[0].Entry!.IsDirectory);
        Assert.Equal(5, rows[0].Entry!.FileCount);
        Assert.Equal(2, rows[0].Entry!.FolderCount);
        Assert.True(rows[1].Entry!.IsDirectory);
        Assert.Equal(0, rows[1].Entry!.Size);
        Assert.Equal(0, rows[1].Entry!.Allocated);
        Assert.False(rows[2].Entry!.IsDirectory);
    }

    [Fact]
    public void MissingRequiredColumn_ThrowsBeforeReadingRows()
    {
        var csv = "文件名称,大小,修改时间,属性,文件,文件夹\r\nC:\\a,1,2024/1/2,----,,\r\n";

        var exception = Assert.Throws<CsvHeaderException>(() => Parse(csv));

        Assert.Contains("分配", exception.Message);
    }

    [Fact]
    public void MalformedRows_AreSkippedWithWarnings_AndRowNumbers()
    {
        var csv = Header +
                  "C:\\good.txt,100,4096,2024/1/2 15:04,----,,\r\n" +
                  "C:\\bad-size.txt,abc,4096,2024/1/2 15:04,----,,\r\n" +
                  "C:\\bad-date.txt,100,4096,not-a-date,----,,\r\n" +
                  ",100,4096,2024/1/2 15:04,----,,\r\n";

        var rows = ParseAll(csv);

        Assert.Equal(4, rows.Count);
        Assert.NotNull(rows[0].Entry);
        Assert.Null(rows[1].Entry);
        Assert.Null(rows[2].Entry);
        Assert.Null(rows[3].Entry);
        Assert.Contains("第 3 行", rows[1].Warning);
        Assert.Contains("大小无法解析", rows[1].Warning);
        Assert.Contains("第 4 行", rows[2].Warning);
        Assert.Contains("修改时间无法解析", rows[2].Warning);
        Assert.Contains("第 5 行", rows[3].Warning);
    }

    [Theory]
    [InlineData("2024/1/2 15:04", 2024, 1, 2, 15, 4)]
    [InlineData("2024/1/2 15:04:05", 2024, 1, 2, 15, 4)]
    [InlineData("2024/1/2", 2024, 1, 2, 0, 0)]
    [InlineData("2024/1/2 3:04 下午", 2024, 1, 2, 15, 4)]
    [InlineData("2024-01-02 15:04", 2024, 1, 2, 15, 4)]
    public void Parses_LocalizedDates(string dateText, int year, int month, int day, int hour, int minute)
    {
        var csv = Header + $"C:\\a.txt,1,4096,{dateText},----,,\r\n";

        var row = Assert.Single(ParseAll(csv));

        Assert.NotNull(row.Entry!.Modified);
        var actual = row.Entry.Modified.Value;
        var truncated = new DateTime(actual.Year, actual.Month, actual.Day, actual.Hour, actual.Minute, 0);
        Assert.Equal(new DateTime(year, month, day, hour, minute, 0), truncated);
    }

    [Fact]
    public void BlankModifiedTime_IsNull()
    {
        var csv = Header + "C:\\a.txt,1,4096,,----,,\r\n";

        var row = Assert.Single(ParseAll(csv));

        Assert.Null(row.Entry!.Modified);
    }

    [Fact]
    public void BlankLines_AreSkippedSilently()
    {
        var csv = Header + "\r\n\r\nC:\\a.txt,1,4096,2024/1/2 15:04,----,,\r\n\r\n";

        var rows = ParseAll(csv);

        Assert.Single(rows);
    }

    [Fact]
    public void UnclosedQuote_ThrowsCsvParseException()
    {
        var csv = Header + "\"C:\\broken.txt,1,4096,2024/1/2 15:04,----,,\r\n";

        Assert.Throws<CsvParseException>(() => ParseAll(csv));
    }

    [Fact]
    public void LargeExport_StreamsAndReportsProgress()
    {
        var rowCount = 100_000;
        var sb = new StringBuilder(Header);
        for (var i = 0; i < rowCount; i++)
        {
            sb.Append("C:\\file-").Append(i).Append(".bin,").Append(i).Append(',').Append(i * 2)
              .Append(",2024/1/2 15:04,----,,\r\n");
        }

        var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
        var progress = new List<ImportProgress>();

        using var stream = new MemoryStream(bytes);
        var parser = new WizTreeCsvParser(stream, new Progress<ImportProgress>(progress.Add));
        var parsed = parser.ReadDataRows().Count(row => row.Entry != null);

        Assert.Equal(rowCount, parsed);
        Assert.NotEmpty(progress);
        Assert.Equal(bytes.LongLength, progress[^1].TotalBytes);
        Assert.Equal(rowCount, progress[^1].Rows);
    }
}
