using IkuyoPet.Core.Reminders;
using IkuyoPet.Pet;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetReminderFeedbackTests
{
    [Fact]
    public void KeepsTwentyOriginalMessagesForEachCompletionKind()
    {
        Assert.Equal(20, PetReminderFeedback.WaterCompletionMessages.Count);
        Assert.Equal(20, PetReminderFeedback.ActivityCompletionMessages.Count);
        Assert.Equal(
            PetReminderFeedback.WaterCompletionMessages.Count,
            PetReminderFeedback.WaterCompletionMessages.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            PetReminderFeedback.ActivityCompletionMessages.Count,
            PetReminderFeedback.ActivityCompletionMessages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PicksWaterCompletionOnlyFromWaterPool()
    {
        var feedback = Enumerable.Range(0, 20)
            .Select(index => PetReminderPresenter.BuildFeedback(
                ReminderAction.Complete,
                "water",
                new Random(index)))
            .ToArray();

        Assert.All(feedback, text =>
            Assert.Contains(text, PetReminderFeedback.WaterCompletionMessages));
        Assert.DoesNotContain(feedback, text =>
            PetReminderFeedback.ActivityCompletionMessages.Contains(text));
    }

    [Fact]
    public void PicksActivityCompletionOnlyFromActivityPool()
    {
        var feedback = Enumerable.Range(0, 20)
            .Select(index => PetReminderPresenter.BuildFeedback(
                ReminderAction.Complete,
                "activity",
                new Random(index)))
            .ToArray();

        Assert.All(feedback, text =>
            Assert.Contains(text, PetReminderFeedback.ActivityCompletionMessages));
        Assert.DoesNotContain(feedback, text =>
            PetReminderFeedback.WaterCompletionMessages.Contains(text));
    }
}
