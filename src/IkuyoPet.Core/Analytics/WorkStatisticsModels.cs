namespace IkuyoPet.Core.Analytics;

public enum WorkStatisticsPeriod
{
    Day,
    Week,
    Month,
}

public sealed record WorkApplicationUsage(
    string ProcessName,
    string DisplayName,
    int ActiveSeconds,
    double Share)
{
    public TimeSpan ActiveTime => TimeSpan.FromSeconds(ActiveSeconds);

    public string DurationText =>
        $"{(int)ActiveTime.TotalHours}小时{ActiveTime.Minutes}分钟";
}

public sealed record WorkStatistics(
    DateOnly StartDate,
    DateOnly EndDateExclusive,
    int TotalActiveSeconds,
    IReadOnlyList<WorkApplicationUsage> TopApplications)
{
    public TimeSpan TotalWorkTime => TimeSpan.FromSeconds(TotalActiveSeconds);
}

public interface IWorkStatisticsQueryService
{
    Task<WorkStatistics> GetAsync(
        DateOnly selectedDate,
        WorkStatisticsPeriod period,
        CancellationToken cancellationToken);
}
