namespace IkuyoPet.Pet.Interaction;

public readonly record struct PetInteractionThrottleDecision(
    bool ShowImmediately,
    string? Text,
    DateTimeOffset NextDueAt);

public sealed class PetInteractionThrottle(TimeSpan interval)
{
    private readonly TimeSpan interval = interval;
    private DateTimeOffset? nextDueAt;
    private string? pendingText;

    public PetInteractionThrottle()
        : this(TimeSpan.FromSeconds(5))
    {
    }

    public PetInteractionThrottleDecision Offer(string text, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        if (nextDueAt is null || now >= nextDueAt.Value)
        {
            nextDueAt = now + interval;
            pendingText = null;
            return new(true, text, nextDueAt.Value);
        }

        pendingText = text;
        return new(false, null, nextDueAt.Value);
    }

    public string? Flush(DateTimeOffset now)
    {
        if (nextDueAt is null || now < nextDueAt.Value || pendingText is null)
            return null;

        var text = pendingText;
        pendingText = null;
        nextDueAt = now + interval;
        return text;
    }

    public void Reset()
    {
        nextDueAt = null;
        pendingText = null;
    }
}
