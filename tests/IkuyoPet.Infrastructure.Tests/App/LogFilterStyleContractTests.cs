using System;
using System.IO;
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
}
