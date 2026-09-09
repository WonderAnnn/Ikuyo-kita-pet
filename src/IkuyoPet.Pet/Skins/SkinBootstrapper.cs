using System.IO;
using IkuyoPet.Core.Skins;

namespace IkuyoPet.Pet.Skins;

public sealed record SkinBootstrapResult(SkinAssetSet? Assets, string? Error)
{
    public bool UsesPlaceholder => Assets is null;
}

public sealed class SkinBootstrapper
{
    private readonly SkinPackageValidator validator;
    private readonly string skinsRoot;

    public SkinBootstrapper(SkinPackageValidator validator, string skinsRoot)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.skinsRoot = Path.GetFullPath(skinsRoot ?? throw new ArgumentNullException(nameof(skinsRoot)));
    }

    public SkinAssetSet? Resolve(SkinSelection? selection) => ResolveWithDiagnostics(selection).Assets;

    public SkinBootstrapResult ResolveWithDiagnostics(SkinSelection? selection)
    {
        if (selection is null) return new(null, "尚未选择皮肤，使用内置占位符。");
        if (!IsSafeSegment(selection.Id) || !IsSafeSegment(selection.Version)) return new(null, "皮肤标识包含不安全路径片段，使用内置占位符。");
        var directory = Path.Combine(skinsRoot, selection.Id, selection.Version);
        var validation = validator.Validate(directory);
        if (!validation.IsValid || validation.Manifest is null)
        {
            var reason = validation.Errors.Count == 0 ? "皮肤清单不可用。" : string.Join("；", validation.Errors);
            return new(null, $"皮肤加载失败：{reason} 使用内置占位符。");
        }
        if (!string.Equals(validation.Manifest.Id, selection.Id, StringComparison.Ordinal) || !string.Equals(validation.Manifest.Version, selection.Version, StringComparison.Ordinal))
            return new(null, "皮肤清单标识与选择不一致，使用内置占位符。");
        return new(new SkinAssetSet(selection.Id, selection.Version, directory, Path.Combine(directory, "idle.png"), Path.Combine(directory, "remind.png"), Optional(directory, "done.png"), Optional(directory, "click.png")), null);
    }

    private static string? Optional(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? path : null;
    }

    private static bool IsSafeSegment(string value) => !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !value.Contains('/') && !value.Contains('\\');
}