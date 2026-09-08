using System.Windows;
using System.Windows.Controls;
using IkuyoPet.Core.Dashboard;

namespace IkuyoPet.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel? viewModel = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.LoadTodayAsync(DateOnly.FromDateTime(DateTime.Today));
            await viewModel.LoadSettingsAsync();
        }
    }

    private void NavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page }) return;
        var pages = new Dictionary<MainWindowPage, UIElement>
        {
            [MainWindowPage.Today] = TodayPanel,
            [MainWindowPage.Log] = LogPanel,
            [MainWindowPage.Rules] = RulesPanel,
            [MainWindowPage.Pet] = PetPanel,
            [MainWindowPage.Settings] = SettingsPanel,
        };
        if (!Enum.TryParse<MainWindowPage>(page, out var selected)) return;
        if (DataContext is MainWindowViewModel viewModel) viewModel.Navigate(selected);
        foreach (var panel in pages.Values) panel.Visibility = Visibility.Collapsed;
        pages[selected].Visibility = Visibility.Visible;
    }
}
