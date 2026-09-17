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

        Assert.Contains("SelectStatisticsPeriodCommand", view);
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
    public void LogViewExposesAStylePdfExport()
    {
        var view = Read("src/IkuyoPet.App/Views/LogView.xaml");
        var viewModel = Read("src/IkuyoPet.App/MainWindowViewModel.cs");

        Assert.Contains("ExportPdfCommand", view);
        Assert.Contains("PdfExportStatus", view);
        Assert.Contains("PdfExportRequested", viewModel);
        Assert.Contains("PdfLogExportSnapshot", viewModel);
    }
    [Fact]
    public void LogFilterBarKeepsFiltersLeftAndExportRightOnOneControlRow()
    {
        var view = Read("src/IkuyoPet.App/Views/LogView.xaml");

        Assert.Contains("<StackPanel Grid.Row=\"0\" Grid.Column=\"0\" Orientation=\"Horizontal\"", view, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"0\" Grid.Column=\"1\"", view, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", view, StringComparison.Ordinal);
        Assert.Equal(4, view.Split("Height=\"32\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LogFilterBarKeepsThreeFiltersOnFirstRow()
    {
        var view = Read("src/IkuyoPet.App/Views/LogView.xaml");

        Assert.Contains("<RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/>", view);
        Assert.Contains("<StackPanel Grid.Row=\"0\" Grid.Column=\"0\" Orientation=\"Horizontal\"", view);
        Assert.Contains("<StackPanel Grid.Row=\"1\" Grid.Column=\"0\" Grid.ColumnSpan=\"2\" Orientation=\"Horizontal\"", view);
    }
    [Fact]
    public void LogFilterControlsPinToTheSameTopAlignment()
    {
        var view = Read("src/IkuyoPet.App/Views/LogView.xaml");

        Assert.Contains("<DatePicker Height=\"32\" VerticalAlignment=\"Top\"", view);
        Assert.Equal(2, view.Split("<ComboBox Height=\"32\" VerticalAlignment=\"Top\"", StringSplitOptions.None).Length - 1);
    }
    [Fact]
    public void LogFilterStylesSuppressSystemBlueSelection()
    {
        var styles = Read("src/IkuyoPet.App/Themes/LogFilterStyles.xaml");

        Assert.Contains(@"FocusVisualStyle=""{x:Null}""", styles);
        Assert.Contains(@"SelectionBrush=""#FFFFD9E8""", styles);
        Assert.Contains(@"SelectionTextBrush=""#FF263247""", styles);
    }
    [Fact]
    public void LogComboBoxUsesTransparentPressedTemplate()
    {
        var styles = Read("src/IkuyoPet.App/Themes/LogFilterStyles.xaml");

        Assert.Contains("<ToggleButton.Template>", styles);
        Assert.Contains("<ControlTemplate TargetType=\"ToggleButton\">", styles);
        Assert.Contains("Background=\"Transparent\" BorderBrush=\"Transparent\"", styles);
    }

    [Fact]
    public void SettingsViewExplainsProcessFieldsAndOffersLiveDiagnostics()
    {
        var settings = Read("src/IkuyoPet.App/Views/SettingsView.xaml");

        Assert.Contains("进程名（用于匹配）", settings);
        Assert.Contains("显示名（仅用于日志）", settings);
        Assert.Contains("RunningProcesses", settings);
        Assert.Contains("RefreshRunningProcessesCommand", settings);
        Assert.Contains("UseSelectedProcessCommand", settings);
        Assert.Contains("TestProcessTrackingCommand", settings);
        Assert.Contains("ProcessTestStatus", settings);
        Assert.Contains("不读取窗口标题或内容", settings);
    }
    [Fact]
    public void MainAndSettingsViewsExposeDurationAndVersionBindings()
    {
        var today = Read("src/IkuyoPet.App/Views/TodayView.xaml");
        var log = Read("src/IkuyoPet.App/Views/LogView.xaml");
        var settings = Read("src/IkuyoPet.App/Views/SettingsView.xaml");
        var app = Read("src/IkuyoPet.App/App.xaml");
        var project = Read("src/IkuyoPet.App/IkuyoPet.App.csproj");

        Assert.Contains("TotalWorkDurationText", today);
        Assert.Contains("TodayWorkDurationText", today);
        Assert.Contains("WorkStatisticsRangeText", log);
        Assert.Contains("VersionText", settings);
        Assert.Contains("FocusVisualStyle", app);
        Assert.Contains("<Version>0.1.4</Version>", project);
    }
    [Fact]
    public void PetBubbleIsAboveDraggableHitArea()
    {
        var pet = Read("src/IkuyoPet.Pet/PetWindow.xaml");

        Assert.Contains("Panel.ZIndex=\"10\"", pet);
        Assert.Contains("Panel.ZIndex=\"0\"", pet);
    }
}
