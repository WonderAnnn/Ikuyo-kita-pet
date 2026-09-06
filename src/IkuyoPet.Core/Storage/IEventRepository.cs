using IkuyoPet.Core.Reminders;

namespace IkuyoPet.Core.Storage;

public interface IEventRepository
{
    Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(
        DateOnly day,
        CancellationToken cancellationToken);
}