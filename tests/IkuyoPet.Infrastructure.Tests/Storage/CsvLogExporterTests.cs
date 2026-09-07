using System.IO;
using System.Text;
using IkuyoPet.Infrastructure.Storage;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Storage;

public sealed class CsvLogExporterTests
{
    [Fact]
    public void SerializesHeaderAndColumnsInFixedOrder()
    {
        var csv = CsvLogExporter.Serialize([
            new CsvLogRow(
                new DateOnly(2026, 9, 6),
                new TimeOnly(10, 20, 30),
                "water",
                "pet",
                "completed",
                "喝水了")]);

        Assert.Equal(
            "日期,时间,类型,渠道,动作,备注\r\n2026-09-06,10:20:30,water,pet,completed,喝水了\r\n",
            csv);
    }

    [Fact]
    public void EscapesCommaQuoteAndLineBreaksAccordingToRfc4180()
    {
        var csv = CsvLogExporter.Serialize([
            new CsvLogRow(
                new DateOnly(2026, 9, 6),
                new TimeOnly(10, 20, 30),
                "类,型",
                "渠\"道",
                "动作\r\n换行",
                "备,注\"A")]);

        Assert.Equal(
            "日期,时间,类型,渠道,动作,备注\r\n" +
            "2026-09-06,10:20:30,\"类,型\",\"渠\"\"道\",\"动作\r\n换行\",\"备,注\"\"A\"\r\n",
            csv);
    }

    [Fact]
    public void PreservesUnicodeAndEmptyFieldsWithoutLeakingSensitiveColumns()
    {
        var csv = CsvLogExporter.Serialize([
            new CsvLogRow(
                new DateOnly(2026, 9, 6),
                new TimeOnly(0, 0),
                "喜多郁代",
                null,
                string.Empty,
                null)]);

        Assert.Contains("喜多郁代", csv);
        Assert.Contains("2026-09-06,00:00:00,喜多郁代,,,", csv);
        Assert.DoesNotContain("window_title", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("window_content", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("screenshot", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sleep", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("medical", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WritesUtf8CrlfFileWhenRequested()
    {
        var rows = new[]
        {
            new CsvLogRow(
                new DateOnly(2026, 9, 6),
                new TimeOnly(10, 20, 30),
                "activity",
                "tray",
                "snoozed",
                "稍后再提醒"),
        };

        var path = Path.Combine(Path.GetTempPath(), $"ikuyo-pet-csv-{Guid.NewGuid():N}.csv");
        await using (var stream = File.Create(path))
        {
            await CsvLogExporter.ExportAsync(rows, stream, TestContext.Current.CancellationToken);
        }

        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, TestContext.Current.CancellationToken);
        Assert.EndsWith("\r\n", text);
        Assert.Contains("activity", text);
        Assert.Contains("稍后再提醒", text);
    }
}

