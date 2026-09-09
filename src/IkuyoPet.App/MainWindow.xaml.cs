using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using IkuyoPet.Core.Dashboard;

namespace IkuyoPet.App;

public partial class MainWindow : Window
{
    public event EventHandler? MinimizeRequested;

    public MainWindow(MainWindowViewModel? viewModel = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.LoadTodayAsync(DateOnly.FromDateTime(DateTime.Today));
                await viewModel.LoadSettingsAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Ikuyo Pet dashboard load failed: {exception}");
            var errorPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IkuyoPet",
                "startup-error.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(errorPath)!);
            System.IO.File.AppendAllText(errorPath, $"[{DateTimeOffset.Now:O}] Dashboard load: {exception}{Environment.NewLine}");
            MessageBox.Show(
                $"今日数据加载失败，主界面仍可使用：{exception.Message}",
                "Ikuyo Pet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

    protected override void OnClosed(EventArgs e)
    {
        StateChanged -= OnStateChanged;
        base.OnClosed(e);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }
}

