using System.IO;
using IkuyoPet.Core.Skins;

namespace IkuyoPet.Pet.Skins;

public sealed class SkinBootstrapper
{
    private readonly SkinPackageValidator validator;
    private readonly string skinsRoot;

    public SkinBootstrapper(SkinPackageValidator validator, string skinsRoot)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.skinsRoot = Path.GetFullPath(skinsRoot ?? throw new ArgumentNullException(nameof(skinsRoot)));
    }

    public SkinAssetSet? Resolve(SkinSelection? selection)
    {
        if (selection is null || !IsSafeSegment(selection.Id) || !IsSafeSegment(selection.Version)) return null;
        var directory = Path.Combine(skinsRoot, selection.Id, selection.Version);
        var validation = validator.Validate(directory);
        if (!validation.IsValid || validation.Manifest is null) return null;
        if (!string.Equals(validation.Manifest.Id, selection.Id, StringComparison.Ordinal) || !string.Equals(validation.Manifest.Version, selection.Version, StringComparison.Ordinal)) return null;
        return new SkinAssetSet(selection.Id, selection.Version, directory, Path.Combine(directory, "idle.png"), Path.Combine(directory, "remind.png"), Optional(directory, "done.png"), Optional(directory, "click.png"));
    }

    private static string? Optional(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? path : null;
    }

    private static bool IsSafeSegment(string value) => !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !value.Contains('/') && !value.Contains('\\');
}