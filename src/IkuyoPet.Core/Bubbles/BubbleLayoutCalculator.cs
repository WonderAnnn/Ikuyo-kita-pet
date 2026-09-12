using System.Text;

namespace IkuyoPet.Core.Bubbles;

public static class BubbleLayoutCalculator
{
    private const double CharacterWidth = 16;
    private const double LineHeight = 24;
    private const double HorizontalPadding = 28;
    private const double VerticalPadding = 22;

    public static BubbleLayoutResult Calculate(
        BubbleThemeDefinition theme,
        string? text,
        IReadOnlyList<string>? actionLabels = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var normalizedText = text ?? string.Empty;
        var actionText = actionLabels is null || actionLabels.Count == 0
            ? string.Empty
            : string.Join("  ·  ", actionLabels.Where(label => !string.IsNullOrWhiteSpace(label)));
        var fullText = string.IsNullOrEmpty(actionText)
            ? normalizedText
            : string.IsNullOrEmpty(normalizedText) ? actionText : $"{normalizedText}  ·  {actionText}";
        var estimatedCharacters = fullText.EnumerateRunes().Count();
        var explicitLines = Math.Max(1, fullText.Count(character => character == '\n') + 1);
        var desiredWidth = Math.Max(theme.MinContentWidth, estimatedCharacters * CharacterWidth + HorizontalPadding);
        var contentWidth = Math.Min(theme.MaxContentWidth, desiredWidth);
        var safeWidth = Math.Max(CharacterWidth, contentWidth * theme.TextSafeArea.Width);
        var wrappedLines = Math.Max(explicitLines, (int)Math.Ceiling(Math.Max(1, estimatedCharacters) * CharacterWidth / safeWidth));
        var desiredHeight = wrappedLines * LineHeight + VerticalPadding;
        var contentHeight = Math.Max(desiredHeight, theme.MinContentHeight);
        var textHeight = wrappedLines * LineHeight;
        var textWidth = Math.Max(CharacterWidth, contentWidth * theme.TextSafeArea.Width);
        var textLeft = contentWidth * theme.TextSafeArea.Left;
        var textTop = Math.Max(0, (contentHeight - textHeight) / 2);
        var windowWidth = contentWidth + HorizontalPadding * 2;
        var windowHeight = contentHeight + VerticalPadding * 2;
        return new BubbleLayoutResult(contentWidth, contentHeight, windowWidth, windowHeight,
            new BubbleTextArea(textLeft, textTop, textWidth, textHeight), wrappedLines);
    }
}
