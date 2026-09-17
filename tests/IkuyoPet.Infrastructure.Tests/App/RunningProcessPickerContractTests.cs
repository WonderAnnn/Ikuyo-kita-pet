using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Core.WorkTracking;
using IkuyoPet.Infrastructure.Windows;
using Xunit;
using AppMainWindowViewModel = IkuyoPet.App.MainWindowViewModel;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class RunningProcessPickerContractTests
{
    [Fact]
    public void SettingsViewWiresUsableRunningProcessPicker()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "IkuyoPet.App", "Views", "SettingsView.xaml"));
        var styles = File.ReadAllText(Path.Combine(root, "src", "IkuyoPet.App", "Themes", "LogFilterStyles.xaml"));

        Assert.Contains("ItemsSource=\"{Binding RunningProcesses}\"", view, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedRunningProcess, Mode=TwoWay}\"", view, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RefreshRunningProcessesCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding UseSelectedProcessCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("TextSearch.TextPath=\"ProcessName\"", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"当前正在运行的进程\"", view, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}\"", styles, StringComparison.Ordinal);
        Assert.Contains("IsOpen=\"{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}\"", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void RunningProcessPopupKeepsScrollBarInsideControlWidth()
    {
        var root = FindRepositoryRoot();
        var styles = File.ReadAllText(Path.Combine(root, "src", "IkuyoPet.App", "Themes", "LogFilterStyles.xaml"));

        Assert.Contains("Width=\"{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}\"", styles, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", styles, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAndUseSelectedProcessFillsBothManualFields()
    {
        var inspector = new StubInspector([
            new RunningProcessInfo("WINWORD", 201),
            new RunningProcessInfo("pycharm64", 101)
        ]);
        var viewModel = CreateViewModel(inspector);

        viewModel.RefreshRunningProcessesCommand.Execute(null);
        viewModel.SelectedRunningProcess = viewModel.RunningProcesses.Single(item => item.ProcessId == 201);
        viewModel.UseSelectedProcessCommand.Execute(null);

        Assert.Equal("WINWORD", viewModel.NewProcessName);
        Assert.Equal("WINWORD", viewModel.NewDisplayName);
        Assert.Contains("PID 201", viewModel.ProcessTestStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshRunningProcessesWhenInspectorFailsShowsErrorWithoutDiscardingManualInput()
    {
        var viewModel = CreateViewModel(new ThrowingInspector());
        viewModel.NewProcessName = "manual.exe";
        viewModel.NewDisplayName = "手动程序";

        viewModel.RefreshRunningProcessesCommand.Execute(null);

        Assert.Equal("manual.exe", viewModel.NewProcessName);
        Assert.Equal("手动程序", viewModel.NewDisplayName);
        Assert.Contains("读取运行进程失败", viewModel.ProcessTestStatus, StringComparison.Ordinal);
    }

    private static AppMainWindowViewModel CreateViewModel(IRunningProcessInspector inspector)
    {
        return new AppMainWindowViewModel(new EmptyDashboard(), processInspector: inspector);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "IkuyoPet.App")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class StubInspector(IReadOnlyList<RunningProcessInfo> processes) : IRunningProcessInspector
    {
        public IReadOnlyList<RunningProcessInfo> GetRunningProcesses() => processes;
        public IReadOnlyList<RunningProcessInfo> GetRunningProcesses(bool forceRefresh = false) => processes;

        public ProcessObservation CaptureForeground() =>
            new(string.Empty, false, TimeSpan.Zero, false, false);
    }

    private sealed class ThrowingInspector : IRunningProcessInspector
    {
        public IReadOnlyList<RunningProcessInfo> GetRunningProcesses() => throw new UnauthorizedAccessException("denied");
        public IReadOnlyList<RunningProcessInfo> GetRunningProcesses(bool forceRefresh = false) => throw new UnauthorizedAccessException("denied");

        public ProcessObservation CaptureForeground() =>
            new(string.Empty, false, TimeSpan.Zero, false, false);
    }

    private sealed class EmptyDashboard : IDashboardQueryService
    {
        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardSnapshot(day, [], 0, 0, 0, 0, TimeSpan.Zero));
    }
}
