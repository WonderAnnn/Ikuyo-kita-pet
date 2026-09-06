namespace IkuyoPet.Pet.Skins;

public sealed record SkinManifest(
    string Id,
    string Name,
    string Version,
    string Author,
    string License,
    int CanvasWidth,
    int CanvasHeight,
    int Fps);
