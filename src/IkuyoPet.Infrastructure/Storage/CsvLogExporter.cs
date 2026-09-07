using System.Globalization;
using System.Text;

namespace IkuyoPet.Infrastructure.Storage;

public sealed record CsvLogRow(
    DateOnly Date,
    TimeOnly Time,
    string? Type,
    string? Channel,
    string? Action,
    string? Note);

public static class CsvLogExporter
{
    private static readonly string Header = "日期,时间,类型,渠道,动作,备注";

    public static string Serialize(IEnumerable<CsvLogRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        builder.Append(Header).Append("\r\n");
        foreach (var row in rows)
        {
            builder.Append(Escape(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                .Append(',')
                .Append(Escape(row.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)))
                .Append(',')
                .Append(Escape(row.Type))
                .Append(',')
                .Append(Escape(row.Channel))
                .Append(',')
                .Append(Escape(row.Action))
                .Append(',')
                .Append(Escape(row.Note))
                .Append("\r\n");
        }

        return builder.ToString();
    }

    public static async Task ExportAsync(
        IEnumerable<CsvLogRow> rows,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(output);

        await using var writer = new StreamWriter(
            output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        await writer.WriteAsync(Serialize(rows).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        var needsQuotes = text.AsSpan().IndexOfAny([',', '"', '\r', '\n']) >= 0;
        if (!needsQuotes)
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
