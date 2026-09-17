namespace IkuyoPet.Core.Dashboard;

public sealed class MainWindowState
{
    public MainWindowPage CurrentPage { get; private set; } = MainWindowPage.Today;

    public void Navigate(MainWindowPage page)
    {
        if (!Enum.IsDefined(page))
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "Unsupported main-window page.");
        }

        CurrentPage = page;
    }
}
