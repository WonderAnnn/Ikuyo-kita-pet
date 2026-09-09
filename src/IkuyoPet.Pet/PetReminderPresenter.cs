using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;

namespace IkuyoPet.Pet;

public sealed record PetReminderAction(ReminderAction Action, string Label);

public sealed record PetReminderView(
    ReminderDue Due,
    string Text,
    IReadOnlyList<PetReminderAction> Actions);

public interface IPetWindowHost
{
    bool IsVisible { get; }
    bool OpenedMainWindow { get; }

    Task ShowAsync(PetReminderView view, CancellationToken cancellationToken);

    Task ShowFeedbackAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;

    void RestoreIdle() { }

    void Hide();
}

public sealed class PetReminderPresenter(IPetWindowHost host) : IReminderPresenter
{
    public string Channel => "pet";

    public async Task ShowAsync(ReminderDue due, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(due);
        cancellationToken.ThrowIfCancellationRequested();

        var view = new PetReminderView(
            due,
            due.Message,
            due.Actions
                .Select(option => new PetReminderAction(option.Action, option.Label))
                .ToArray());

        await host.ShowAsync(view, cancellationToken);
    }

    public async Task ShowFeedbackAsync(ReminderAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await host.ShowFeedbackAsync(BuildFeedback(action), cancellationToken);
        }
        finally
        {
            host.RestoreIdle();
        }
    }

    public static string BuildFeedback(ReminderAction action) => action switch
    {
        ReminderAction.Complete => "完成啦！ദ്ദി˶>𖥦<)✧",
        ReminderAction.Snooze => "那就稍等一下下嘛～(,,•́ . •̀,,)",
        ReminderAction.Skip => "好吧，这次先放过自己 ʕ.•᷅ࡇ•᷄.ʔ",
        _ => "我会等你回来哒～(,,•́ . •̀,,)",
    };
}
