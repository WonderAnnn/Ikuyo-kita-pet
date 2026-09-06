using Xunit;
using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Infrastructure.Windows;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class WindowsNotificationPresenterTests
{
    [Fact]
    public async Task MapsReminderDueToNotificationRequest()
    {
        var sink = new RecordingNotificationSink();
        var presenter = new WindowsNotificationPresenter(sink);
        var due = new ReminderDue(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "water",
            "喝口水再继续吧～",
            [
                new(ReminderAction.Complete, "现在喝"),
                new(ReminderAction.Snooze, "十分钟后"),
                new(ReminderAction.Skip, "今天先不啦"),
            ]);

        await presenter.ShowAsync(due, TestContext.Current.CancellationToken);

        Assert.NotNull(sink.LastRequest);
        Assert.Equal(due.EventId, sink.LastRequest!.EventId);
        Assert.Equal("Ikuyo Pet", sink.LastRequest.Title);
        Assert.Equal(due.Message, sink.LastRequest.Message);
        Assert.Equal(
            new[] { ReminderAction.Complete, ReminderAction.Snooze, ReminderAction.Skip },
            sink.LastRequest.Actions);
    }

    [Fact]
    public async Task CancellationStopsBeforeCallingNotificationSink()
    {
        var sink = new RecordingNotificationSink();
        var presenter = new WindowsNotificationPresenter(sink);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            presenter.ShowAsync(
                new ReminderDue(Guid.NewGuid(), "activity", "起来动一动吧～", []),
                cancellation.Token));

        Assert.Null(sink.LastRequest);
    }

    private sealed class RecordingNotificationSink : INotificationSink
    {
        public NotificationRequest? LastRequest { get; private set; }

        public Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.CompletedTask;
        }
    }
}
