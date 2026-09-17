using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IkuyoPet.App.Export;

public static class AStylePdfLogExporter
{
    private const int PageWidth = 794;
    private const int PageHeight = 1123;
    private const double PdfPageWidth = 595.28;
    private const double PdfPageHeight = 841.89;

    private static readonly Color Ink = Color.FromRgb(38, 50, 71);
    private static readonly Color Muted = Color.FromRgb(104, 119, 141);
    private static readonly Color Pink = Color.FromRgb(194, 86, 146);
    private static readonly Color PalePink = Color.FromRgb(255, 240, 246);
    private static readonly Color Coral = Color.FromRgb(241, 124, 134);
    private static readonly Color Cream = Color.FromRgb(255, 249, 242);
    private static readonly Color Line = Color.FromRgb(230, 234, 240);
    private static readonly Color Green = Color.FromRgb(90, 167, 141);

    public static byte[] Serialize(
        PdfLogExportSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            DrawPage(drawing, snapshot, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bitmap = new RenderTargetBitmap(
            PageWidth,
            PageHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        cancellationToken.ThrowIfCancellationRequested();

        var rgb = ToRgb(bitmap);
        return BuildPdf(rgb, PageWidth, PageHeight, snapshot, cancellationToken);
    }

    private static void DrawPage(
        DrawingContext drawing,
        PdfLogExportSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        drawing.DrawRectangle(Brush(Cream), null, new Rect(0, 0, PageWidth, PageHeight));
        drawing.DrawRectangle(Brush(Pink), null, new Rect(0, 0, PageWidth, 210));
        DrawPick(drawing, PageWidth - 92, 52, 0.95, Colors.White);
        DrawStaff(drawing, 70, 153, 300, Color.FromRgb(255, 226, 235));
        DrawText(drawing, "Ikuyo Pet · 日志导出", 30, Colors.White, 70, 62);
        DrawText(drawing, "非官方氛围主题，数据为本机离线日志", 13, Colors.White, 70, 106);

        DrawText(drawing, "导出范围", 13, Muted, 70, 242);
        DrawText(drawing, $"{snapshot.RangeText} · 周期：{snapshot.PeriodLabel}", 16, Ink, 70, 266);

        DrawRoundedCard(drawing, 70, 310, 210, 96, PalePink);
        DrawRoundedCard(drawing, 292, 310, 210, 96, Color.FromRgb(239, 249, 245));
        DrawRoundedCard(drawing, 514, 310, 210, 96, Color.FromRgb(245, 247, 250));
        DrawText(drawing, "喝水完成", 12, Muted, 88, 332);
        DrawText(drawing, $"{snapshot.HydrationCompletedCount} 次", 25, Pink, 88, 357);
        DrawText(drawing, "活动完成", 12, Muted, 310, 332);
        DrawText(drawing, $"{snapshot.ActivityCompletedCount} 次", 25, Green, 310, 357);
        DrawText(drawing, "有效工作", 12, Muted, 532, 332);
        DrawText(drawing, snapshot.TotalWorkText, 25, Ink, 532, 357);

        DrawText(drawing, "前五应用前台时间", 18, Ink, 70, 445);
        var applications = snapshot.Applications.Take(5).ToArray();
        if (applications.Length == 0)
        {
            DrawText(drawing, "当前范围还没有有效前台工作记录", 13, Muted, 70, 485);
        }
        else
        {
            var maxSeconds = Math.Max(1, applications.Max(application => application.ActiveTime.TotalSeconds));
            for (var index = 0; index < applications.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var application = applications[index];
                var y = 486 + index * 34;
                DrawText(drawing, TrimText(application.DisplayName, 15), 12, Ink, 70, y);
                drawing.DrawRoundedRectangle(
                    Brush(Color.FromRgb(248, 232, 240)),
                    null,
                    new Rect(225, y + 2, 405, 14),
                    7,
                    7);
                var width = 405 * application.ActiveTime.TotalSeconds / maxSeconds;
                drawing.DrawRoundedRectangle(Brush(Pink), null, new Rect(225, y + 2, width, 14), 7, 7);
                DrawText(drawing, application.DurationText, 12, Muted, 650, y);
            }
        }

        var tableTop = 680;
        DrawText(drawing, "提醒日志", 18, Ink, 70, tableTop);
        var tableY = tableTop + 38;
        drawing.DrawRectangle(Brush(PalePink), null, new Rect(70, tableY, 654, 34));
        DrawText(drawing, "时间", 11, Muted, 84, tableY + 9);
        DrawText(drawing, "类型", 11, Muted, 165, tableY + 9);
        DrawText(drawing, "渠道", 11, Muted, 244, tableY + 9);
        DrawText(drawing, "结果", 11, Muted, 350, tableY + 9);
        DrawText(drawing, "备注", 11, Muted, 432, tableY + 9);

        var entries = snapshot.Entries.Take(8).ToArray();
        if (entries.Length == 0)
        {
            DrawText(drawing, "这一天还没有日志", 13, Muted, 84, tableY + 58);
        }
        else
        {
            for (var index = 0; index < entries.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = entries[index];
                var y = tableY + 34 + index * 34;
                drawing.DrawRectangle(Brush(index % 2 == 0 ? Colors.White : Color.FromRgb(255, 252, 253)), null, new Rect(70, y, 654, 34));
                DrawText(drawing, entry.Time.ToString("HH:mm", CultureInfo.InvariantCulture), 11, Ink, 84, y + 9);
                DrawText(drawing, TrimText(entry.Kind, 8), 11, Ink, 165, y + 9);
                DrawText(drawing, TrimText(entry.Channel, 10), 11, Muted, 244, y + 9);
                DrawText(drawing, TrimText(entry.Outcome, 8), 11, entry.Outcome == "完成" ? Pink : Muted, 350, y + 9);
                DrawText(drawing, TrimText(entry.Note, 23), 11, Muted, 432, y + 9);
            }
        }

        var footerY = PageHeight - 68;
        drawing.DrawLine(new Pen(Brush(Line), 1), new Point(70, footerY), new Point(724, footerY));
        DrawText(drawing, "今天也按自己的节奏来吧！喝口水，走两步，再回来继续。 (^_^)", 11, Ink, 70, footerY + 18);
        DrawText(drawing, "Ikuyo Pet · 本地离线日志示例 · 不记录睡眠、医疗报告或窗口内容", 9, Muted, 70, footerY + 42);
        DrawText(drawing, "样例 A · 非官方元素", 9, Muted, 610, footerY + 42);
    }

    private static void DrawRoundedCard(DrawingContext drawing, double x, double y, double width, double height, Color color) =>
        drawing.DrawRoundedRectangle(Brush(color), new Pen(Brush(Line), 1), new Rect(x, y, width, height), 16, 16);

    private static void DrawText(DrawingContext drawing, string text, double size, Color color, double x, double y)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            size,
            Brush(color),
            1.0);
        drawing.DrawText(formatted, new Point(x, y));
    }

    private static void DrawPick(DrawingContext drawing, double x, double y, double scale, Color color)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(x, y), true, true);
            context.LineTo(new Point(x + 28 * scale, y + 8 * scale), true, false);
            context.LineTo(new Point(x + 18 * scale, y + 52 * scale), true, false);
            context.LineTo(new Point(x - 10 * scale, y + 45 * scale), true, false);
        }
        drawing.DrawGeometry(Brush(color), null, geometry);
        drawing.DrawEllipse(Brush(Pink), null, new Point(x + 8 * scale, y + 19 * scale), 4 * scale, 4 * scale);
    }

    private static void DrawStaff(DrawingContext drawing, double x, double y, double width, Color color)
    {
        var pen = new Pen(Brush(color), 2);
        for (var index = 0; index < 5; index++)
        {
            drawing.DrawLine(pen, new Point(x, y + index * 8), new Point(x + width, y + index * 8));
        }
        foreach (var position in new[] { 0.16, 0.42, 0.69, 0.88 })
        {
            drawing.DrawEllipse(Brush(color), null, new Point(x + width * position, y + 15), 5, 5);
        }
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    private static string TrimText(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";

    private static byte[] ToRgb(BitmapSource bitmap)
    {
        var bgra = new byte[PageWidth * PageHeight * 4];
        bitmap.CopyPixels(bgra, PageWidth * 4, 0);
        var rgb = new byte[PageWidth * PageHeight * 3];
        var target = 0;
        for (var source = 0; source < bgra.Length; source += 4)
        {
            rgb[target++] = bgra[source + 2];
            rgb[target++] = bgra[source + 1];
            rgb[target++] = bgra[source];
        }
        return rgb;
    }

    private static byte[] BuildPdf(
        byte[] rgb,
        int width,
        int height,
        PdfLogExportSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(rgb);
        }

        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R /ViewerPreferences << /DisplayDocTitle true >> >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PdfPageWidth.ToString(CultureInfo.InvariantCulture)} {PdfPageHeight.ToString(CultureInfo.InvariantCulture)}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>"),
            BuildImageObject(compressed.ToArray(), width, height),
            BuildContentObject(),
            Ascii($"<< /Title (Ikuyo Pet A-style log export) /Subject (NonOfficial private preview; {snapshot.RangeText}) /Producer (Ikuyo Pet offline exporter) >>"),
        };

        cancellationToken.ThrowIfCancellationRequested();
        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n");
            output.Write(objects[index]);
            WriteAscii(output, "\nendobj\n");
        }

        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] BuildImageObject(byte[] compressed, int width, int height)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        stream.Write(compressed);
        WriteAscii(stream, "\nendstream");
        return stream.ToArray();
    }

    private static byte[] BuildContentObject()
    {
        var content = $"q\n{PdfPageWidth.ToString(CultureInfo.InvariantCulture)} 0 0 {PdfPageHeight.ToString(CultureInfo.InvariantCulture)} 0 0 cm\n/Im0 Do\nQ\n";
        var bytes = Ascii(content);
        return Ascii($"<< /Length {bytes.Length} >>\nstream\n{content}endstream");
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }
}
