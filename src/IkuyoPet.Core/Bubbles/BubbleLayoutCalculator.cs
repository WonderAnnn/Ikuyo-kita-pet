using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IkuyoPet.Core.Bubbles;

public static class BubbleLayoutCalculator
{
    private const double CharacterWidth = 16;
    private const double LineHeight = 24;
    private const double HorizontalPadding = 28;
    private const double VerticalPadding = 22;

    public const double ContentHeightHardCap = 1080;

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
        var paragraphs = fullText.Split('\n');
        var paragraphLengths = paragraphs
            .Select(paragraph => paragraph.EnumerateRunes().Count())
            .ToArray();
        var longestParagraph = Math.Max(1, paragraphLengths.Max());
        var desiredWidth = Math.Max(theme.MinContentWidth, longestParagraph * CharacterWidth + HorizontalPadding);
        var contentWidth = Math.Min(theme.MaxContentWidth, desiredWidth);
        var safeWidth = Math.Max(CharacterWidth, contentWidth * theme.TextSafeArea.Width);
        var wrappedLines = paragraphLengths.Sum(length =>
            Math.Max(1, (int)Math.Ceiling(length * CharacterWidth / safeWidth)));
        var desiredHeight = wrappedLines * LineHeight + VerticalPadding;
        var effectiveHardCap = Math.Max(ContentHeightHardCap, theme.MinContentHeight);
        var contentHeight = Math.Clamp(desiredHeight, theme.MinContentHeight, effectiveHardCap);
        var textHeight = Math.Min(wrappedLines * LineHeight, contentHeight - VerticalPadding);
        var textWidth = Math.Max(CharacterWidth, contentWidth * theme.TextSafeArea.Width);
        var textLeft = contentWidth * theme.TextSafeArea.Left;
        var textTop = Math.Max(0, (contentHeight - textHeight) / 2);
        var windowWidth = contentWidth + HorizontalPadding * 2;
        var windowHeight = contentHeight + VerticalPadding * 2;
        return new BubbleLayoutResult(contentWidth, contentHeight, windowWidth, windowHeight,
            new BubbleTextArea(textLeft, textTop, textWidth, textHeight), wrappedLines);
    }
}
