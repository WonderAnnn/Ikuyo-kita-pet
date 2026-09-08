using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.Storage;

namespace IkuyoPet.Infrastructure.Windows;

public sealed class ReminderLoop
{
    private readonly IEventRepository repository;
    private readonly ReminderPresentationRouter router;
    private readonly Func<bool> isPetEnabled;
    private readonly TimeProvider timeProvider;
    private readonly TimeZoneInfo timeZone;
    private readonly TimeSpan tickInterval;
    private readonly Func<bool> isPaused;
    private readonly Dictionary<Guid, DateTimeOffset> lastDisplayedAt = [];

    public ReminderLoop(
        IEventRepository repository,
        ReminderPresentationRouter router,
        Func<bool> isPetEnabled,
        TimeProvider timeProvider,
        TimeZoneInfo timeZone,
        TimeSpan? tickInterval = null,
        Func<bool>? isPaused = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.isPetEnabled = isPetEnabled ?? throw new ArgumentNullException(nameof(isPetEnabled));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        this.tickInterval = tickInterval ?? TimeSpan.FromSeconds(30);
        this.isPaused = isPaused ?? (() => false);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.tickInterval, TimeSpan.Zero);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessOnceAsync(cancellationToken).ConfigureAwait(false);
            if (tickInterval == TimeSpan.Zero)
            {
                await Task.Yield();
            }
            else
            {
                await Task.Delay(tickInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        if (isPaused()) return;
        var rules = await repository.ReadReminderRulesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!rule.Enabled)
            {
                continue;
            }

            var previous = lastDisplayedAt.TryGetValue(rule.Id, out var displayedAt)
                ? displayedAt
                : (DateTimeOffset?)null;
            var due = ReminderScheduleCalculator.GetDue(rule, now, previous, timeZone);
            if (due is null)
            {
                continue;
            }

            var petEnabled = isPetEnabled();
            var item = new ReminderEvent(
                due.EventId,
                rule.Id,
                now,
                now,
                router.GetChannel(petEnabled),
                ReminderOutcome.None,
                null,
                0,
                null,
                now);
            await repository.AppendReminderAsync(item, cancellationToken).ConfigureAwait(false);
            var channel = await router.ShowAsync(due, petEnabled, cancellationToken).ConfigureAwait(false);
            if (channel != item.Channel &&
                !await repository.UpdateReminderChannelAsync(
                    item.Id,
                    item.Channel,
                    channel,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Reminder event '{item.Id}' channel could not be updated.");
            }

            lastDisplayedAt[rule.Id] = now;
        }
    }
}
