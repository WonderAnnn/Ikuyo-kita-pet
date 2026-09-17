using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class TodayViewManualActionStyleTests
{
    [Fact]
    public void ManualActionsUseMatchingCompactButtonGeometry()
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = File.ReadAllText(Path.Combine(repository, "src", "IkuyoPet.App", "Views", "TodayView.xaml"));

        Assert.Equal(2, source.Split("Width=\"96\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, source.Split("Height=\"36\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, source.Split("Padding=\"0\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("Command=\"{Binding ManualWaterCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ManualActivityCommand}\"", source, StringComparison.Ordinal);
    }
}