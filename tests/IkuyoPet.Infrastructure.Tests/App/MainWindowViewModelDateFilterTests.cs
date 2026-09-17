using IkuyoPet.Core.Dashboard;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class MainWindowViewModelDateFilterTests
{
    [Theory]
    [InlineData("2026/9/12", 2026, 9, 12)]
    [InlineData("2026/09/02", 2026, 9, 2)]
    [InlineData("2026-09-12", 2026, 9, 12)]
    public void TryParseLogDateAcceptsOnlySupportedRealDates(
        string text,
        int year,
        int month,
        int day)
    {
        Assert.True(IkuyoPet.App.MainWindowViewModel.TryParseLogDate(text, out var parsed));
        Assert.Equal(new DateTime(year, month, day), parsed);
    }

    [Theory]
    [InlineData("12sdf2026")]
    [InlineData("2026/2/30")]
    [InlineData("26/9/12")]
    [InlineData("2026.9.12")]
    [InlineData("")]
    public void TryParseLogDateRejectsMalformedOrImpossibleDates(string text)
    {
        Assert.False(IkuyoPet.App.MainWindowViewModel.TryParseLogDate(text, out _));
    }

    [Fact]
    public void InvalidDateTextDoesNotFilterOrRequestPdfExport()
    {
        var dashboard = new RecordingDashboard();
        var viewModel = new IkuyoPet.App.MainWindowViewModel(dashboard);
        var originalDate = viewModel.SelectedDate;
        var exportRequested = false;
        viewModel.PdfExportRequested += (_, _) => exportRequested = true;

        viewModel.SelectedDateText = "12sdf2026";
        viewModel.ExportPdfCommand.Execute(null);

        Assert.Equal(originalDate, viewModel.SelectedDate);
        Assert.Empty(dashboard.RequestedDays);
        Assert.False(exportRequested);
        Assert.True(viewModel.HasLogDateValidationError);
        Assert.Contains("yyyy/M/d", viewModel.LogDateValidationMessage);
        Assert.Contains("无法导出", viewModel.PdfExportStatus);
    }

    [Fact]
    public void ValidDateTextUpdatesFilterAndAllowsPdfExport()
    {
        var dashboard = new RecordingDashboard();
        var viewModel = new IkuyoPet.App.MainWindowViewModel(dashboard);
        var exportRequested = false;
        viewModel.PdfExportRequested += (_, _) => exportRequested = true;

        viewModel.SelectedDateText = "2026/9/11";
        viewModel.ExportPdfCommand.Execute(null);

        Assert.Equal(new DateTime(2026, 9, 11), viewModel.SelectedDate);
        Assert.Contains(new DateOnly(2026, 9, 11), dashboard.RequestedDays);
        Assert.False(viewModel.HasLogDateValidationError);
        Assert.Equal(string.Empty, viewModel.LogDateValidationMessage);
        Assert.True(exportRequested);
    }

    [Fact]
    public void LogViewBindsStrictDateTextAndShowsValidationMessage()
    {
        var view = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "IkuyoPet.App", "Views", "LogView.xaml"));

        Assert.Contains("SelectedDateText", view);
        Assert.Contains("LogDateValidationMessage", view);
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", view);
    }

    private sealed class RecordingDashboard : IDashboardQueryService
    {
        public List<DateOnly> RequestedDays { get; } = [];

        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken)
        {
            RequestedDays.Add(day);
            return Task.FromResult(new DashboardSnapshot(day, [], 0, 0, 0, 0, TimeSpan.Zero));
        }
    }
}
