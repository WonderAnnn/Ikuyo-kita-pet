namespace IkuyoPet.Pet.Interaction;

public enum PetBubbleState
{
    Idle,
    Interaction,
    Feedback,
    Reminder,
}

public sealed class PetBubbleStateMachine
{
    public PetBubbleState State { get; private set; } = PetBubbleState.Idle;

    public bool TryBeginInteraction()
    {
        if (State is not (PetBubbleState.Idle or PetBubbleState.Interaction)) return false;
        State = PetBubbleState.Interaction;
        return true;
    }

    public void BeginReminder() => State = PetBubbleState.Reminder;

    public bool BeginFeedback()
    {
        State = PetBubbleState.Feedback;
        return true;
    }

    public void RestoreIdle() => State = PetBubbleState.Idle;
}
