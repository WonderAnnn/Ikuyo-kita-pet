using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Threading;
using IkuyoPet.App.Export;
using IkuyoPet.Core.Dashboard;

namespace IkuyoPet.App;

public partial class MainWindow : Window
{
    public event EventHandler? MinimizeRequested;

    private System.Windows.Threading.DispatcherTimer? diagnosticsTimer;

    public MainWindow(MainWindowViewModel? viewModel = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (viewModel is not null)
        {
            viewModel.PdfExportRequested += OnPdfExportRequested;
            viewModel.BackupRequested += OnBackupRequested;
            viewModel.RestoreBrowseRequested += OnRestoreBrowseRequested;
            viewModel.ExportDataRequested += OnExportDataRequested;
        }
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

            StartDiagnosticsTimer();
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

    private void OnPdfExportRequested(object? sender, EventArgs e)
    {        if (DataContext is not MainWindowViewModel viewModel) return;

        var snapshot = viewModel.CreatePdfExportSnapshot();
        var dialog = new SaveFileDialog
        {
            Title = "导出 Ikuyo Pet 日志 PDF",
            Filter = "PDF 文件 (*.pdf)|*.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = $"IkuyoPet-日志-{snapshot.StartDate:yyyyMMdd}.pdf",
        };
        if (dialog.ShowDialog(this) != true)
        {
            viewModel.SetPdfExportStatus("已取消 PDF 导出。");
            return;
        }

        try
        {
            var bytes = AStylePdfLogExporter.Serialize(snapshot);
            File.WriteAllBytes(dialog.FileName, bytes);
            viewModel.SetPdfExportStatus($"已导出：{System.IO.Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"PDF export failed: {exception}");
            viewModel.SetPdfExportStatus($"PDF 导出失败：{exception.Message}");
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
            [MainWindowPage.Diagnostics] = DiagnosticsPanel,
            [MainWindowPage.Pet] = PetPanel,
            [MainWindowPage.Settings] = SettingsPanel,
        };
        if (!Enum.TryParse<MainWindowPage>(page, out var selected)) return;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Navigate(selected);
            if (selected == MainWindowPage.Diagnostics)
            {
                _ = viewModel.RefreshDiagnosticsAsync();
            }
        }
        foreach (var panel in pages.Values) panel.Visibility = Visibility.Collapsed;
        pages[selected].Visibility = Visibility.Visible;
    }

    private void NumericTextBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsAsciiDigits(e.Text);
    }

    private void NumericTextBoxPasting(object sender, DataObjectPastingEventArgs e)
    {
        var pastedText = e.DataObject.GetData(DataFormats.UnicodeText) as string
            ?? e.DataObject.GetData(DataFormats.Text) as string;
        if (!IsAsciiDigits(pastedText)) e.CancelCommand();
    }

    private static bool IsAsciiDigits(string? text) =>
        !string.IsNullOrEmpty(text) &&
        text.All(character => char.IsDigit(character) && character is >= '0' and <= '9');

    private void OnBackupRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;

        var dialog = new SaveFileDialog
        {
            Title = "备份数据库",
            Filter = "SQLite 数据库 (*.db)|*.db",
            AddExtension = true,
            DefaultExt = ".db",
            FileName = $"ikuyo-pet-{DateTime.Now:yyyyMMdd-HHmmss}.db",
        };
        if (dialog.ShowDialog(this) != true) return;
        _ = viewModel.BackupNowAsync(dialog.FileName);
    }

    private void OnRestoreBrowseRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;

        var dialog = new OpenFileDialog
        {
            Title = "选择要恢复的备份",
            Filter = "SQLite 数据库 (*.db)|*.db",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        var choice = MessageBox.Show(
            $"恢复会覆盖当前全部本地数据，并自动保留一份恢复前的安全副本，完成后应用会重启。\n\n确认用以下备份恢复？\n{dialog.FileName}",
            "Ikuyo Pet",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes) return;
        viewModel.RaiseRestoreConfirmed(dialog.FileName);
    }

    private void OnExportDataRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;

        var dialog = new SaveFileDialog
        {
            Title = "导出全部数据 (JSON)",
            Filter = "JSON 文件 (*.json)|*.json",
            AddExtension = true,
            DefaultExt = ".json",
            FileName = $"IkuyoPet-数据-{DateTime.Now:yyyyMMdd}.json",
        };
        if (dialog.ShowDialog(this) != true) return;
        _ = viewModel.ExportDataToAsync(dialog.FileName);
    }

    private void StartDiagnosticsTimer()
    {
        if (diagnosticsTimer is not null) return;
        diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        diagnosticsTimer.Tick += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel viewModel)
            {
                return;
            }

            viewModel.RefreshTimeSensitiveDashboardText();
            if (viewModel.CurrentPage != MainWindowPage.Diagnostics) return;
            _ = viewModel.RefreshDiagnosticsAsync();
        };
        diagnosticsTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        StateChanged -= OnStateChanged;
        diagnosticsTimer?.Stop();
        diagnosticsTimer = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PdfExportRequested -= OnPdfExportRequested;
            viewModel.BackupRequested -= OnBackupRequested;
            viewModel.RestoreBrowseRequested -= OnRestoreBrowseRequested;
            viewModel.ExportDataRequested -= OnExportDataRequested;
        }
        base.OnClosed(e);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }
}

