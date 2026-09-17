namespace IkuyoPet.App.Export;

public sealed record PdfLogExportApplication(
    string ProcessName,
    string DisplayName,
    TimeSpan ActiveTime)
{
    public string DurationText =>
        $"{(int)ActiveTime.TotalHours}小时{ActiveTime.Minutes}分钟";
}

public sealed record PdfLogExportEntry(
    TimeOnly Time,
    string Kind,
    string Channel,
    string Outcome,
    string Note);

public sealed record PdfLogExportSnapshot(
    DateOnly StartDate,
    DateOnly EndDateInclusive,
    string PeriodLabel,
    TimeSpan TotalWorkTime,
    int HydrationCompletedCount,
    int ActivityCompletedCount,
    IReadOnlyList<PdfLogExportApplication> Applications,
    IReadOnlyList<PdfLogExportEntry> Entries)
{
    public string RangeText => StartDate == EndDateInclusive
        ? $"北京时间 {StartDate.Year}年{StartDate.Month}月{StartDate.Day}日"
        : $"北京时间 {StartDate.Year}年{StartDate.Month}月{StartDate.Day}日-{EndDateInclusive.Year}年{EndDateInclusive.Month}月{EndDateInclusive.Day}日";

    public string TotalWorkText =>
        $"{(int)TotalWorkTime.TotalHours}小时{TotalWorkTime.Minutes}分钟";
}
