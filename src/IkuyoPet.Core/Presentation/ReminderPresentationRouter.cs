namespace IkuyoPet.Core.Presentation;

public sealed class ReminderPresentationRouter(
    IReminderPresenter petPresenter,
    IReminderPresenter notificationPresenter)
{
    public string GetChannel(bool petEnabled) =>
        petEnabled ? petPresenter.Channel : notificationPresenter.Channel;

    public async Task<string> ShowAsync(
        ReminderDue due,
        bool petEnabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(due);

        var selected = petEnabled ? petPresenter : notificationPresenter;
        try
        {
            await selected.ShowAsync(due, cancellationToken);
            return selected.Channel;
        }
        catch when (petEnabled && !cancellationToken.IsCancellationRequested)
        {
            await notificationPresenter.ShowAsync(due, cancellationToken);
            return notificationPresenter.Channel;
        }
    }
}
