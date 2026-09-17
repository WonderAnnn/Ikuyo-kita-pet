using IkuyoPet.Pet.Interaction;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetInteractionThrottleTests
{
    [Fact]
    public void FirstOfferIsImmediateAndStartsFiveSecondWindow()
    {
        var throttle = new PetInteractionThrottle(TimeSpan.FromSeconds(5));
        var now = Now();

        var result = throttle.Offer("first", now);

        Assert.True(result.ShowImmediately);
        Assert.Equal("first", result.Text);
        Assert.Null(throttle.Flush(now.AddSeconds(4.99)));
    }

    [Fact]
    public void OffersWithinWindowKeepOnlyLatestTextAndFlushOnceAtDeadline()
    {
        var throttle = new PetInteractionThrottle(TimeSpan.FromSeconds(5));
        var now = Now();
        throttle.Offer("first", now);

        var second = throttle.Offer("second", now.AddSeconds(1));
        var third = throttle.Offer("third", now.AddSeconds(2));

        Assert.False(second.ShowImmediately);
        Assert.Equal(now.AddSeconds(5), second.NextDueAt);
        Assert.False(third.ShowImmediately);
        Assert.Equal(now.AddSeconds(5), third.NextDueAt);
        Assert.Null(throttle.Flush(now.AddSeconds(4.99)));
        Assert.Equal("third", throttle.Flush(now.AddSeconds(5)));
        Assert.Null(throttle.Flush(now.AddSeconds(5.01)));
    }

    [Fact]
    public void ResetDropsPendingOffer()
    {
        var throttle = new PetInteractionThrottle(TimeSpan.FromSeconds(5));
        var now = Now();
        throttle.Offer("first", now);
        throttle.Offer("pending", now.AddSeconds(1));

        throttle.Reset();

        Assert.Null(throttle.Flush(now.AddSeconds(10)));
        var result = throttle.Offer("after-reset", now.AddSeconds(10));
        Assert.True(result.ShowImmediately);
        Assert.Equal("after-reset", result.Text);
    }
    private static DateTimeOffset Now() =>
        new(2026, 9, 10, 10, 0, 0, TimeSpan.FromHours(8));

}
