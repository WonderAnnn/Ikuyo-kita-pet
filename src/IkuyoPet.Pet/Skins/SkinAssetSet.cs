namespace IkuyoPet.Pet.Skins;

public sealed record SkinAssetSet(
    string SkinId,
    string Version,
    string PackageDirectory,
    string IdlePath,
    string RemindPath,
    string? DonePath = null,
    string? ClickPath = null);