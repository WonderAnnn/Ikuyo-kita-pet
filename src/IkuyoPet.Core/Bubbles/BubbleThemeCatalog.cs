namespace IkuyoPet.Core.Bubbles;

public sealed class BubbleThemeCatalog
{
    public static IReadOnlyList<BubbleThemeDefinition> BuiltInThemes { get; } =
    [
        new BubbleThemeDefinition(
            "cloud-chibi", "云朵晕乎", "1.0.0", "cloud-chibi/bubble.png", "cloud-chibi/bubble.png",
            2752, 1536, new BubbleSliceInsets(520, 250, 520, 280), new BubbleSafeArea(0.28, 0.24, 0.44, 0.40),
            220, 520, 72, 240),
        new BubbleThemeDefinition(
            "cloud-guitar", "吉他现场", "1.0.0", "cloud-guitar/bubble.png", "cloud-guitar/bubble.png",
            2048, 2048, new BubbleSliceInsets(620, 420, 420, 420), new BubbleSafeArea(0.42, 0.25, 0.45, 0.42),
            320, 760, 88, 320),
        new BubbleThemeDefinition(
            "cloud-smile", "元气微笑", "1.0.0", "cloud-smile/bubble.png", "cloud-smile/bubble.png",
            4096, 4096, new BubbleSliceInsets(900, 900, 900, 900), new BubbleSafeArea(0.30, 0.25, 0.48, 0.44),
            260, 680, 96, 320),
    ];

    private readonly string assetsRoot;

    public BubbleThemeCatalog(string assetsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);
        this.assetsRoot = Path.GetFullPath(assetsRoot);
    }

    public BubbleThemeDefinition Default => BuiltInThemes[0];

    public BubbleThemeDefinition Resolve(string? id)
    {
        var theme = BuiltInThemes.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return theme is not null && File.Exists(Path.Combine(assetsRoot, theme.ResourcePath)) ? theme : Default;
    }

    public string ResolveResourcePath(BubbleThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Path.Combine(assetsRoot, theme.ResourcePath);
    }
}
