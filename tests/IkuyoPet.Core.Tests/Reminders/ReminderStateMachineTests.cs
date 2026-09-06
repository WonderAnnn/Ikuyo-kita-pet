using IkuyoPet.Core.Reminders;
using Xunit;

namespace IkuyoPet.Core.Tests.Reminders;

public sealed class ReminderStateMachineTests
{
    private readonly ReminderStateMachine machine = new(maxAttempts: 3);

    [Fact]
    public void NoResponseOnThirdAttemptEndsAsUnanswered()
    {
        var state = ReminderState.Pending(attempt: 3);

        var result = machine.Apply(state, ReminderAction.NoResponse);

        Assert.Equal(ReminderOutcome.Unanswered, result.Outcome);
        Assert.False(result.ShouldRetry);
    }

    [Fact]
    public void CompleteEndsReminderWithoutRetry()
    {
        var result = machine.Apply(ReminderState.Pending(), ReminderAction.Complete);

        Assert.Equal(ReminderOutcome.Completed, result.Outcome);
        Assert.False(result.ShouldRetry);
    }

    [Fact]
    public void SnoozeRequestsAnotherAttempt()
    {
        var result = machine.Apply(ReminderState.Pending(), ReminderAction.Snooze);

        Assert.Equal(ReminderOutcome.Snoozed, result.Outcome);
        Assert.True(result.ShouldRetry);
    }

    [Fact]
    public void SkipEndsReminderWithoutRetry()
    {
        var result = machine.Apply(ReminderState.Pending(), ReminderAction.Skip);

        Assert.Equal(ReminderOutcome.Skipped, result.Outcome);
        Assert.False(result.ShouldRetry);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NoResponseBeforeLimitRequestsRetry(int attempt)
    {
        var result = machine.Apply(ReminderState.Pending(attempt), ReminderAction.NoResponse);

        Assert.Equal(ReminderOutcome.None, result.Outcome);
        Assert.True(result.ShouldRetry);
    }
}