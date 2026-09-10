using System;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class LogFilterStyleContractTests
{
    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));

    [Fact]
    public void LogFilterStylesDefineTheFilterContract()
    {
        var styles = Read("src/IkuyoPet.App/Themes/LogFilterStyles.xaml");

        Assert.Contains("x:Key=\"LogFilterBarStyle\"", styles);
        Assert.Contains("x:Key=\"LogDatePickerStyle\"", styles);
        Assert.Contains("x:Key=\"LogComboBoxStyle\"", styles);
        Assert.Contains("x:Key=\"LogComboBoxItemStyle\"", styles);
        Assert.Contains("PART_TextBox", styles);
        Assert.Contains("PART_Button", styles);
        Assert.Contains("PART_Popup", styles);
    }

    [Fact]
    public void LogDateTextBoxCentersItsTextVertically()
    {
        var styles = Read("src/IkuyoPet.App/Themes/LogFilterStyles.xaml");

        Assert.Contains("VerticalContentAlignment=\"Center\"", styles);
        Assert.Contains("FontSize=\"14\"", styles);
    }

    [Fact]
    public void LogViewKeepsBindingsAndFilterOptions()
    {
        var view = Read("src/IkuyoPet.App/Views/LogView.xaml");

        Assert.Contains("SelectedDate", view);
        Assert.Contains("KindFilter", view);
        Assert.Contains("OutcomeFilter", view);
        Assert.Contains("SelectedValuePath=\"Content\"", view);
        foreach (var option in new[] { "全部", "喝水", "活动", "工作", "全部结果", "完成", "搁置", "跳过", "未响应", "待处理" })
            Assert.Contains($"Content=\"{option}\"", view);
        Assert.Contains("LogFilterBarStyle", view);
        Assert.Contains("LogDatePickerStyle", view);
        Assert.Contains("LogComboBoxStyle", view);
        Assert.Contains("AutomationProperties.Name", view);
    }

    [Fact]
    public void LogViewContainsPeriodStatisticsAndTopApplications()
    {
        var view = Read("src/IkuyoPet.App/Views/LogView.xaml");

        Assert.Contains("StatisticsPeriodIndex", view);
        Assert.Contains("WorkStatisticsTotalText", view);
        Assert.Contains("TopApplicationStats", view);
        Assert.Contains("ProgressBar", view);
        Assert.Contains(@"Content=""日""", view);
        Assert.Contains(@"Content=""周""", view);
        Assert.Contains(@"Content=""月""", view);
    }

    [Fact]
    public void LogViewPlacesStatisticsAndTimelineOnSeparateRows()
    {
        var document = XDocument.Parse(Read("src/IkuyoPet.App/Views/LogView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var root = document.Root!.Element(wpf + "Grid");
        Assert.NotNull(root);

        var statisticsPanel = root!.Elements(wpf + "Border")
            .Single(element => element.Descendants(wpf + "TextBlock")
                .Any(text => (string?)text.Attribute("Text") == "工作统计"));
        var timelinePanel = root.Elements(wpf + "Grid")
            .Single(element => element.Descendants(wpf + "ItemsControl")
                .Any(items => (string?)items.Attribute("ItemsSource") == "{Binding TimelineItems}"));

        var statisticsRow = (string?)statisticsPanel.Attribute("Grid.Row");
        var timelineRow = (string?)timelinePanel.Attribute("Grid.Row");
        Assert.NotEqual(statisticsRow, timelineRow);

        var rowCount = root.Element(wpf + "Grid.RowDefinitions")?.Elements(wpf + "RowDefinition").Count() ?? 0;
        Assert.True(int.TryParse(timelineRow, out var row));
        Assert.InRange(row, 0, rowCount - 1);
    }

    [Fact]
    public void LogFilterStylesSuppressSystemBlueSelection()
    {
        var styles = Read("src/IkuyoPet.App/Themes/LogFilterStyles.xaml");

        Assert.Contains(@"FocusVisualStyle=""{x:Null}""", styles);
        Assert.Contains(@"SelectionBrush=""#FFFFD9E8""", styles);
        Assert.Contains(@"SelectionTextBrush=""#FF263247""", styles);
    }
}
