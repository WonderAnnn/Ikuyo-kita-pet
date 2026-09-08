using Xunit;
using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;

namespace IkuyoPet.Core.Tests.Presentation;

public sealed class ReminderPresentationRouterTests
{
    [Theory]
    [InlineData(true, "pet")]
    [InlineData(false, "notification")]
    public async Task RoutesToConfiguredPresenter(bool petEnabled, string expected)
    {
        var pet = new RecordingPresenter("pet");
        var notification = new RecordingPresenter("notification");
        var router = new ReminderPresentationRouter(pet, notification);

        var channel = await router.ShowAsync(
            CreateDue(),
            petEnabled,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, channel);
        Assert.Equal(expected, pet.LastChannel ?? notification.LastChannel);
        Assert.Equal(petEnabled, pet.LastDue is not null);
        Assert.Equal(!petEnabled, notification.LastDue is not null);
    }

    [Fact]
    public async Task FallsBackToNotificationWhenPetPresenterFails()
    {
        var pet = new RecordingPresenter("pet", shouldThrow: true);
        var notification = new RecordingPresenter("notification");
        var router = new ReminderPresentationRouter(pet, notification);

        var channel = await router.ShowAsync(
            CreateDue(),
            petEnabled: true,
            TestContext.Current.CancellationToken);

        Assert.Equal("notification", channel);
        Assert.True(pet.Attempted);
        Assert.Equal("notification", notification.LastChannel);
        Assert.Equal(pet.LastDue, notification.LastDue);
    }

    private static ReminderDue CreateDue() =>
        new(
            Guid.NewGuid(),
            "activity",
            "陪我起来走两分钟嘛～",
            [
                new ReminderActionOption(ReminderAction.Complete, "现在出发"),
                new ReminderActionOption(ReminderAction.Snooze, "让我等你一会儿"),
                new ReminderActionOption(ReminderAction.Skip, "先放过自己"),
            ]);

    private sealed class RecordingPresenter(string channel, bool shouldThrow = false) : IReminderPresenter
    {
        public string Channel => channel;
        public ReminderDue? LastDue { get; private set; }
        public string? LastChannel { get; private set; }
        public bool Attempted { get; private set; }

        public Task ShowAsync(ReminderDue due, CancellationToken cancellationToken)
        {
            Attempted = true;
            LastDue = due;
            LastChannel = Channel;
            if (shouldThrow) throw new InvalidOperationException("pet unavailable");
            return Task.CompletedTask;
        }
    }
}
