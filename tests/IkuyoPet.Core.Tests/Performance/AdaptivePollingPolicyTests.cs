using IkuyoPet.Core.Performance;
using Xunit;

namespace IkuyoPet.Core.Tests.Performance;

public sealed class AdaptivePollingPolicyTests
{
    [Theory]
    [InlineData(false, false, 30)]
    [InlineData(true, false, 60)]
    [InlineData(false, true, 60)]
    public void WorkTrackingIntervalAdaptsToSessionState(bool locked, bool longIdle, int seconds)
    {
        var policy = new AdaptivePollingPolicy();

        Assert.Equal(TimeSpan.FromSeconds(seconds), policy.GetWorkTrackingInterval(locked, longIdle));
    }

    [Fact]
    public void ReminderIntervalNeverExceedsThirtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new AdaptivePollingPolicy().GetReminderInterval());
    }

    [Fact]
    public void DiagnosticsPollingStopsWhenPageIsHidden()
    {
        var policy = new AdaptivePollingPolicy();

        Assert.Equal(TimeSpan.FromSeconds(15), policy.GetDiagnosticsInterval(true));
        Assert.Equal(Timeout.InfiniteTimeSpan, policy.GetDiagnosticsInterval(false));
    }
}
