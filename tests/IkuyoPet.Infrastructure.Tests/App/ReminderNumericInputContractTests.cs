using System.Xml.Linq;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class ReminderNumericInputContractTests
{
    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));

    [Fact]
    public void AllIntervalInputsBlockNonDigitsFromTypingAndPaste()
    {
        var document = XDocument.Parse(Read("src/IkuyoPet.App/MainWindow.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var intervalInputs = document.Descendants(wpf + "TextBox")
            .Where(element => ((string?)element.Attribute("Text"))?.Contains("Interval", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(4, intervalInputs.Length);
        Assert.All(intervalInputs, input =>
        {
            Assert.Equal("NumericTextBoxPreviewTextInput", (string?)input.Attribute("PreviewTextInput"));
            Assert.Equal("NumericTextBoxPasting", (string?)input.Attribute("DataObject.Pasting"));
        });
    }

    [Fact]
    public void ActivityDurationUsesTheSameNumericInputGuard()
    {
        var xaml = Read("src/IkuyoPet.App/MainWindow.xaml");
        var durationBinding = "Text=\"{Binding ActivityRule.ActivityDurationMinutes}\"";
        var bindingIndex = xaml.IndexOf(durationBinding, StringComparison.Ordinal);

        Assert.True(bindingIndex >= 0);
        var inputStart = xaml.LastIndexOf("<TextBox", bindingIndex, StringComparison.Ordinal);
        var inputEnd = xaml.IndexOf("/>", bindingIndex, StringComparison.Ordinal);
        var input = xaml[inputStart..inputEnd];
        Assert.Contains("PreviewTextInput=\"NumericTextBoxPreviewTextInput\"", input);
        Assert.Contains("DataObject.Pasting=\"NumericTextBoxPasting\"", input);
    }

    [Fact]
    public void ReminderCardsExplainTheTwentyFourHourLimit()
    {
        var xaml = Read("src/IkuyoPet.App/MainWindow.xaml");
        Assert.True(
            xaml.Split("范围 1–1440 分钟（不超过24小时）", StringSplitOptions.None).Length - 1 >= 2);
    }

    [Fact]
    public void PasteHandlerCancelsNonNumericContent()
    {
        var codeBehind = Read("src/IkuyoPet.App/MainWindow.xaml.cs");
        Assert.Contains("NumericTextBoxPreviewTextInput", codeBehind);
        Assert.Contains("NumericTextBoxPasting", codeBehind);
        Assert.Contains("char.IsDigit", codeBehind);
        Assert.Contains("CancelCommand", codeBehind);
    }
}
