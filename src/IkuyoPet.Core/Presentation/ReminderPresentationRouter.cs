namespace IkuyoPet.Core.Presentation;

public sealed class ReminderPresentationRouter(
    IReminderPresenter petPresenter,
    IReminderPresenter notificationPresenter)
{
    public async Task ShowAsync(
        ReminderDue due,
        bool petEnabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(due);

        var selected = petEnabled ? petPresenter : notificationPresenter;
        try
        {
            await selected.ShowAsync(due, cancellationToken);
        }
        catch when (petEnabled && !cancellationToken.IsCancellationRequested)
        {
            await notificationPresenter.ShowAsync(due, cancellationToken);
        }
    }
}
