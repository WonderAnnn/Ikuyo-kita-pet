using IkuyoPet.Core.Presentation;
using IkuyoPet.Core.Reminders;
using IkuyoPet.Pet;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetReminderPresenterTests
{
    [Fact]
    public async Task ShowsDueInPetHostWithoutOpeningMainWindow()
    {
        var host = new RecordingPetHost();
        var presenter = new PetReminderPresenter(host);
        var due = new ReminderDue(Guid.NewGuid(), "water", "喝口水再继续吧～", []);

        await presenter.ShowAsync(due, TestContext.Current.CancellationToken);

        Assert.True(host.IsVisible);
        Assert.False(host.OpenedMainWindow);
        Assert.Equal(due, host.LastView!.Due);
        Assert.Equal(due.Message, host.LastView.Text);
    }

    [Fact]
    public void ActionEventRetainsReminderEventId()
    {
        var due = new ReminderDue(Guid.NewGuid(), "activity", "起来活动一下吧～", []);
        var view = new PetReminderView(due, due.Message, []);

        var actionEvent = PetReminderActionEventFactory.Create(view, ReminderAction.Complete);

        Assert.Equal(due.EventId, actionEvent.EventId);
        Assert.Equal(ReminderAction.Complete, actionEvent.Action);
    }
    [Theory]
    [InlineData(ReminderAction.Complete, "ദ്ദി˶>𖥦<)✧")]
    [InlineData(ReminderAction.Snooze, "(,,•́ . •̀,,)")]
    [InlineData(ReminderAction.Skip, "ʕ.•᷅ࡇ•᷄.ʔ")]
    public void BuildsOneScenarioKaomojiAtEnd(ReminderAction action, string kaomoji)
    {
        var feedback = PetReminderPresenter.BuildFeedback(action);

        Assert.EndsWith(kaomoji, feedback, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(feedback, kaomoji));
    }

    [Theory]
    [InlineData(ReminderAction.Complete, "完成啦！ദ്ദി˶>𖥦<)✧")]
    [InlineData(ReminderAction.Snooze, "那就稍等一下下嘛～(,,•́ . •̀,,)")]
    [InlineData(ReminderAction.Skip, "好吧，这次先放过自己 ʕ.•᷅ࡇ•᷄.ʔ")]
    public async Task ShowsActionFeedbackThenRestoresIdleSkin(
        ReminderAction action,
        string expectedText)
    {
        var host = new RecordingPetHost();
        var presenter = new PetReminderPresenter(host);

        await presenter.ShowFeedbackAsync(action, TestContext.Current.CancellationToken);

        Assert.Equal(expectedText, host.LastFeedback);
        Assert.Equal(1, host.IdleRestoreCount);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class RecordingPetHost : IPetWindowHost
    {
        public bool IsVisible { get; private set; }
        public bool OpenedMainWindow => false;
        public PetReminderView? LastView { get; private set; }
        public string? LastFeedback { get; private set; }
        public int IdleRestoreCount { get; private set; }

        public Task ShowAsync(PetReminderView view, CancellationToken cancellationToken)
        {
            LastView = view;
            IsVisible = true;
            return Task.CompletedTask;
        }

        public void Hide() => IsVisible = false;

        public Task ShowFeedbackAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastFeedback = text;
            return Task.CompletedTask;
        }

        public void RestoreIdle()
        {
            IdleRestoreCount++;
        }
    }
}
