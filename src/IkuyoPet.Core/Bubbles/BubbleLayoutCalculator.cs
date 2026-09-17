using System.Text;

namespace IkuyoPet.Core.Bubbles;

public static class BubbleLayoutCalculator
{
    private const double CharacterWidth = 16;
    private const double LineHeight = 24;
    private const double MinimumSafeWidth = 112;
    private const double MaximumSafeWidth = 320;
    private const double MaximumBubbleHeight = 460;

    public const double ContentHeightHardCap = 1080;

    public static BubbleLayoutResult Calculate(
        BubbleThemeDefinition theme,
        string? text,
        IReadOnlyList<string>? actionLabels = null,
        double maximumWindowHeight = MaximumBubbleHeight)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!double.IsFinite(maximumWindowHeight) ||
            maximumWindowHeight < 132 ||
            maximumWindowHeight > MaximumBubbleHeight)
            throw new ArgumentOutOfRangeException(nameof(maximumWindowHeight));

        var actionText = actionLabels is null
            ? string.Empty
            : string.Join("  ·  ", actionLabels.Where(label => !string.IsNullOrWhiteSpace(label)));
        var fullText = string.IsNullOrEmpty(actionText)
            ? text ?? string.Empty
            : string.IsNullOrEmpty(text) ? actionText : $"{text}  ·  {actionText}";
        var lengths = fullText.Split('\n')
            .Select(value => Math.Max(1, value.EnumerateRunes().Count()))
            .ToArray();
        var longest = lengths.Max();
        var aspect = (double)theme.PixelWidth / theme.PixelHeight;
        var targetSafeWidth = Math.Clamp(longest * 4 + 28, MinimumSafeWidth, MaximumSafeWidth);
        var height = Math.Clamp(
            targetSafeWidth / (aspect * theme.TextSafeArea.Width),
            132,
            maximumWindowHeight);

        int lineCount;
        double safeWidth;
        double safeHeight;
        while (true)
        {
            var width = height * aspect;
            safeWidth = width * theme.TextSafeArea.Width;
            safeHeight = height * theme.TextSafeArea.Height;
            lineCount = lengths.Sum(length =>
                Math.Max(1, (int)Math.Ceiling(length * CharacterWidth / safeWidth)));
            if (lineCount * LineHeight <= safeHeight || height >= MaximumBubbleHeight) break;
            if (height >= maximumWindowHeight) break;
            height = Math.Min(maximumWindowHeight, height + 4);
        }

        var windowWidth = height * aspect;
        var contentHeight = lineCount * LineHeight;
        var result = new BubbleLayoutResult(
            safeWidth,
            contentHeight,
            windowWidth,
            height,
            new BubbleTextArea(
                windowWidth * theme.TextSafeArea.Left,
                height * theme.TextSafeArea.Top,
                safeWidth,
                safeHeight),
            lineCount);
        return result with
        {
            RequiresVerticalScroll = contentHeight > safeHeight,
            TextViewportHeight = safeHeight,
        };
    }
}
