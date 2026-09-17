namespace IkuyoPet.Core.Reminders;

public static class ReminderKinds
{
    public static bool IsWallClock(string kind) =>
        string.Equals(kind, "water", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "hydration", StringComparison.OrdinalIgnoreCase);

    public static bool IsActiveWork(string kind) =>
        string.Equals(kind, "activity", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "move", StringComparison.OrdinalIgnoreCase);

    public static string ToDisplayText(string kind) =>
        IsWallClock(kind) ? "喝水"
        : IsActiveWork(kind) ? "活动"
        : kind;
}
