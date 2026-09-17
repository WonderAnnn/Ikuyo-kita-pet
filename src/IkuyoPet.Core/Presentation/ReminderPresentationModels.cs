namespace IkuyoPet.Core.Presentation;

using IkuyoPet.Core.Reminders;

public sealed record ReminderActionOption(ReminderAction Action, string Label);

public sealed record ReminderDue(
    Guid EventId,
    string Kind,
    string Message,
    IReadOnlyList<ReminderActionOption> Actions);

public interface IReminderPresenter
{
    string Channel { get; }

    Task ShowAsync(ReminderDue due, CancellationToken cancellationToken);
}
