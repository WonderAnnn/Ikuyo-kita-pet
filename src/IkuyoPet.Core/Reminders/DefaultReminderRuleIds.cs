namespace IkuyoPet.Core.Reminders;

/// <summary>
/// Fixed identifiers for the two seeded rules. The reminder loop, the settings
/// editor and the diagnostics panel must agree on these so user edits land on
/// the same rows the loop reads.
/// </summary>
public static class DefaultReminderRuleIds
{
    public static readonly Guid Activity =
        Guid.Parse("76b178f2-1700-4f17-b72a-f4b3e9c52d1f");

    public static readonly Guid Hydration =
        Guid.Parse("4f4d3cc4-831f-4b1a-9fc5-04f0a0e2c6b7");
}
