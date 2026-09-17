using System.Reflection;
using System.Threading;
using IkuyoPet.App.Export;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class PdfLogExportTests
{
    [Fact]
    public void SnapshotKeepsOnlyPrivacySafeLogAndStatisticsFields()
    {
        var snapshot = new PdfLogExportSnapshot(
            new DateOnly(2026, 9, 11),
            new DateOnly(2026, 9, 11),
            "日",
            TimeSpan.FromHours(3).Add(TimeSpan.FromMinutes(26)),
            6,
            2,
            [new PdfLogExportApplication("pycharm64", "PyCharm", TimeSpan.FromMinutes(58))],
            [new PdfLogExportEntry(
                new TimeOnly(9, 20),
                "喝水",
                "桌宠",
                "完成",
                "接水并走动两分钟")]);

        Assert.Equal("北京时间 2026年9月11日", snapshot.RangeText);
        Assert.Equal("3小时26分钟", snapshot.TotalWorkText);
        Assert.Contains("喝水", snapshot.Entries[0].Kind);
        var serialized = string.Join('|', snapshot.Entries.Select(entry => entry.Note));
        Assert.DoesNotContain("window_title", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("window_content", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sleep", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("medical", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportCreatesAStylePdfWithPrivacyMetadata()
    {
        var snapshot = new PdfLogExportSnapshot(
            new DateOnly(2026, 9, 11),
            new DateOnly(2026, 9, 11),
            "日",
            TimeSpan.FromMinutes(58),
            1,
            1,
            [new PdfLogExportApplication("WINWORD", "Microsoft Word", TimeSpan.FromMinutes(44))],
            [new PdfLogExportEntry(new TimeOnly(10, 16), "活动", "桌宠", "完成", "离屏活动 5 分钟")]);

        var bytes = RunOnSta(() => AStylePdfLogExporter.Serialize(snapshot));

        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.True(bytes.Length > 2_000);
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("Ikuyo Pet A-style", text);
        Assert.Contains("NonOfficial", text);
        Assert.Contains("/Subtype /Image", text);
        Assert.Contains("/Type /Page", text);
    }

    [Fact]
    public void ExportHonorsCancellationBeforeRendering()
    {
        var snapshot = new PdfLogExportSnapshot(
            new DateOnly(2026, 9, 11),
            new DateOnly(2026, 9, 11),
            "日",
            TimeSpan.Zero,
            0,
            0,
            [],
            []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            RunOnSta(() => AStylePdfLogExporter.Serialize(snapshot, cancellation.Token)));
    }

    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception exception) { failure = exception; }
            finally { completed.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completed.Wait();
        thread.Join();
        if (failure is not null) throw failure;
        return result!;
    }
}
