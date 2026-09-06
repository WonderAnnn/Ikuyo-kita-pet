using IkuyoPet.Core.Reminders;
using IkuyoPet.Core.WorkTracking;

namespace IkuyoPet.Core.Storage;

public interface IEventRepository
{
    Task AppendReminderAsync(ReminderEvent item, CancellationToken cancellationToken);

    Task AppendWorkSessionAsync(WorkSession session, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReminderEvent>> ReadReminderEventsAsync(
        DateOnly day,
        CancellationToken cancellationToken);
}