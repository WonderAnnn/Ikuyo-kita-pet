using IkuyoPet.Core.Presentation;
using Xunit;

namespace IkuyoPet.Core.Tests.Presentation;

public sealed class TrayCommandModelTests
{
    [Fact]
    public void T1ContainsOnlyFourQuietCommands()
    {
        var commands = TrayMenuModel.T1.Select(item => item.Command).ToArray();

        Assert.Equal(
            [TrayCommand.TogglePet, TrayCommand.PauseReminders,
             TrayCommand.OpenToday, TrayCommand.Exit],
            commands);
    }

    [Fact]
    public void T1LabelsDoNotExposeStatisticsOrQuickActions()
    {
        Assert.DoesNotContain(TrayMenuModel.T1, item => item.Label.Contains("喝水", StringComparison.Ordinal));
        Assert.DoesNotContain(TrayMenuModel.T1, item => item.Label.Contains("统计", StringComparison.Ordinal));
    }
}
