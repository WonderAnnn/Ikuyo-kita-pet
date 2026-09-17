using System.Xml.Linq;
using IkuyoPet.Core.Analytics;
using IkuyoPet.Core.Dashboard;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class WorkStatisticsAnchorUiTests
{
    [Fact]
    public async Task PeriodCommandUsesSelectedDateAsAnchorForRollingWindows()
    {
        var statistics = new RecordingStatistics();
        var viewModel = new IkuyoPet.App.MainWindowViewModel(
            new EmptyDashboard(),
            workStatisticsQuery: statistics);
        viewModel.SelectedDateText = "2026/9/10";
        await statistics.WaitForAsync(WorkStatisticsPeriod.Day);

        viewModel.SelectStatisticsPeriodCommand.Execute("1");
        await statistics.WaitForAsync(WorkStatisticsPeriod.Week);
        Assert.Equal(new DateOnly(2026, 9, 10), statistics.LastAnchor);
        Assert.Equal("北京时间 2026年9月4日–2026年9月10日", viewModel.WorkStatisticsRangeText);

        viewModel.SelectStatisticsPeriodCommand.Execute("2");
        await statistics.WaitForAsync(WorkStatisticsPeriod.Month);
        Assert.Equal(new DateOnly(2026, 9, 10), statistics.LastAnchor);
        Assert.Equal("北京时间 2026年8月12日–2026年9月10日", viewModel.WorkStatisticsRangeText);
    }

    [Fact]
    public void StatisticsPeriodButtonsLiveInTheCardHeaderWithAbsoluteRange()
    {
        var viewPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "IkuyoPet.App", "Views", "LogView.xaml");
        var document = XDocument.Parse(File.ReadAllText(viewPath));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var statisticsCard = document.Descendants(wpf + "Border")
            .Single(element => element.Descendants(wpf + "TextBlock")
                .Any(text => (string?)text.Attribute("Text") == "工作统计"));
        var header = statisticsCard.Descendants(wpf + "Grid")
            .First(element => (string?)element.Attribute("Grid.Row") == "0");
        var buttons = header.Descendants(wpf + "RadioButton").ToArray();

        Assert.Equal(["日", "周", "月"], buttons.Select(button => (string?)button.Attribute("Content")));
        Assert.All(buttons, button => Assert.Equal(
            "{Binding SelectStatisticsPeriodCommand}",
            (string?)button.Attribute("Command")));
        Assert.Contains(header.Descendants(wpf + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{Binding WorkStatisticsRangeText}");
    }

    private sealed class EmptyDashboard : IDashboardQueryService
    {
        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardSnapshot(day, [], 0, 0, 0, 0, TimeSpan.Zero));
    }

    private sealed class RecordingStatistics : IWorkStatisticsQueryService
    {
        private readonly List<WorkStatisticsPeriod> observed = [];
        public DateOnly LastAnchor { get; private set; }

        public Task<WorkStatistics> GetAsync(
            DateOnly selectedDate,
            WorkStatisticsPeriod period,
            CancellationToken cancellationToken)
        {
            LastAnchor = selectedDate;
            lock (observed) observed.Add(period);
            var start = period switch
            {
                WorkStatisticsPeriod.Day => selectedDate,
                WorkStatisticsPeriod.Week => selectedDate.AddDays(-6),
                WorkStatisticsPeriod.Month => selectedDate.AddDays(-29),
                _ => throw new ArgumentOutOfRangeException(nameof(period)),
            };
            return Task.FromResult(new WorkStatistics(start, selectedDate.AddDays(1), 0, []));
        }

        public async Task WaitForAsync(WorkStatisticsPeriod period)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                lock (observed)
                {
                    if (observed.Contains(period)) return;
                }
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            Assert.Fail($"Statistics period {period} was not requested.");
        }
    }
}
