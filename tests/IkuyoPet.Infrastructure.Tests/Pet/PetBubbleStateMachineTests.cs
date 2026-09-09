using Xunit;
using IkuyoPet.Pet.Interaction;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetBubbleStateMachineTests
{
    [Fact]
    public void InteractionCanBeginOnlyWhenIdleOrAlreadyInteraction()
    {
        var machine = new PetBubbleStateMachine();
        Assert.True(machine.TryBeginInteraction());
        Assert.True(machine.TryBeginInteraction());
        machine.BeginFeedback();
        Assert.False(machine.TryBeginInteraction());
        machine.BeginReminder();
        Assert.False(machine.TryBeginInteraction());
    }

    [Fact]
    public void ReminderAndFeedbackTakePriorityAndRestoreIdle()
    {
        var machine = new PetBubbleStateMachine();
        machine.BeginReminder();
        machine.BeginFeedback();
        Assert.Equal(PetBubbleState.Feedback, machine.State);
        machine.BeginReminder();
        Assert.Equal(PetBubbleState.Reminder, machine.State);
        machine.RestoreIdle();
        Assert.Equal(PetBubbleState.Idle, machine.State);
    }
}
