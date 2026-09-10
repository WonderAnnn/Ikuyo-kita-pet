using IkuyoPet.Core.Storage;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Core.Analytics;

public sealed class WorkStatisticsQueryService : IWorkStatisticsQueryService
{
    private readonly IEventRepository repository;
    private readonly TimeZoneInfo timeZone;

    public WorkStatisticsQueryService(
        IEventRepository repository,
        TimeZoneInfo? timeZone = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public async Task<WorkStatistics> GetAsync(
        DateOnly selectedDate,
        WorkStatisticsPeriod period,
        CancellationToken cancellationToken)
    {
        var (startDate, endDate) = GetDateRange(selectedDate, period);
        var sessionsById = new Dictionary<Guid, WorkSession>();
        for (var day = startDate; day < endDate; day = day.AddDays(1))
        {
            var sessions = await repository
                .ReadWorkSessionsAsync(day, cancellationToken)
                .ConfigureAwait(false);
            foreach (var session in sessions)
            {
                sessionsById.TryAdd(session.Id, session);
            }
        }

        var rangeStart = ToUtc(startDate.ToDateTime(TimeOnly.MinValue));
        var rangeEnd = ToUtc(endDate.ToDateTime(TimeOnly.MinValue));
        decimal totalTicks = 0;
        var appTicks = new Dictionary<(string ProcessName, string DisplayName), decimal>();

        foreach (var session in sessionsById.Values)
        {
            if (session.ActiveSeconds <= 0 || session.EndedAt <= session.StartedAt)
            {
                continue;
            }

            var sessionStart = session.StartedAt.ToUniversalTime();
            var sessionEnd = session.EndedAt.ToUniversalTime();
            var overlapStart = sessionStart > rangeStart ? sessionStart : rangeStart;
            var overlapEnd = sessionEnd < rangeEnd ? sessionEnd : rangeEnd;
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            var sessionTicks = (decimal)(sessionEnd - sessionStart).Ticks;
            var overlapTicks = (decimal)(overlapEnd - overlapStart).Ticks;
            var contributionTicks = decimal.Truncate(
                (decimal)session.ActiveSeconds * TimeSpan.TicksPerSecond * overlapTicks / sessionTicks);
            if (contributionTicks <= 0)
            {
                continue;
            }

            totalTicks += contributionTicks;
            var key = (session.ProcessName, session.DisplayName);
            appTicks[key] = appTicks.GetValueOrDefault(key) + contributionTicks;
        }

        var totalSeconds = ToSeconds(totalTicks);
        var applications = appTicks
            .Select(item => new WorkApplicationUsage(
                item.Key.ProcessName,
                item.Key.DisplayName,
                ToSeconds(item.Value),
                totalTicks > 0
                    ? (double)item.Value / (double)totalTicks
                    : 0))
            .OrderByDescending(item => item.ActiveSeconds)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        return new WorkStatistics(
            startDate,
            endDate,
            totalSeconds,
            applications);
    }

    private DateTimeOffset ToUtc(DateTime localTime)
    {
        var local = new DateTimeOffset(
            DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
            timeZone.GetUtcOffset(localTime));
        return local.ToUniversalTime();
    }

    private static int ToSeconds(decimal ticks)
    {
        var seconds = decimal.Truncate(ticks / TimeSpan.TicksPerSecond);
        return seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
    }

    private static (DateOnly Start, DateOnly EndExclusive) GetDateRange(
        DateOnly selectedDate,
        WorkStatisticsPeriod period) => period switch
        {
            WorkStatisticsPeriod.Day => (selectedDate, selectedDate.AddDays(1)),
            WorkStatisticsPeriod.Week => GetWeekRange(selectedDate),
            WorkStatisticsPeriod.Month =>
                (new DateOnly(selectedDate.Year, selectedDate.Month, 1),
                 new DateOnly(selectedDate.Year, selectedDate.Month, 1).AddMonths(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
        };

    private static (DateOnly Start, DateOnly EndExclusive) GetWeekRange(DateOnly selectedDate)
    {
        var daysFromMonday = ((int)selectedDate.DayOfWeek + 6) % 7;
        var start = selectedDate.AddDays(-daysFromMonday);
        return (start, start.AddDays(7));
    }
}
