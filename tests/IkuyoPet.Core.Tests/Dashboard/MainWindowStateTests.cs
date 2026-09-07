using IkuyoPet.Core.Dashboard;
using Xunit;

namespace IkuyoPet.Core.Tests.Dashboard;

public sealed class MainWindowStateTests
{
    [Fact]
    public void DefaultsToToday()
    {
        var state = new MainWindowState();

        Assert.Equal(MainWindowPage.Today, state.CurrentPage);
    }

    [Theory]
    [InlineData(MainWindowPage.Today)]
    [InlineData(MainWindowPage.Log)]
    [InlineData(MainWindowPage.Rules)]
    [InlineData(MainWindowPage.Pet)]
    [InlineData(MainWindowPage.Settings)]
    public void NavigatesToEachSupportedPage(MainWindowPage page)
    {
        var state = new MainWindowState();

        state.Navigate(page);

        Assert.Equal(page, state.CurrentPage);
    }

    [Fact]
    public void RejectsValuesOutsideFiveSupportedPages()
    {
        var state = new MainWindowState();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Navigate((MainWindowPage)99));
        Assert.Equal(MainWindowPage.Today, state.CurrentPage);
    }
}
