using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IkuyoPet.Pet.Skins;

public sealed record SkinValidationResult(
    bool IsValid,
    SkinManifest? Manifest,
    IReadOnlyList<string> Errors);

public sealed class SkinPackageValidator
{
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SkinValidationResult Validate(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var errors = new List<string>();
        if (!Directory.Exists(packageDirectory))
        {
            return Invalid($"Package directory does not exist: {packageDirectory}");
        }

        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        SkinManifest? manifest = null;
        if (!File.Exists(manifestPath))
        {
            errors.Add("manifest.json is required.");
        }
        else
        {
            try
            {
                manifest = JsonSerializer.Deserialize<SkinManifest>(
                    File.ReadAllText(manifestPath),
                    jsonOptions);
                if (manifest is null)
                {
                    errors.Add("manifest.json must contain an object.");
                }
            }
            catch (JsonException)
            {
                errors.Add("manifest.json is not valid JSON for SkinManifest.");
            }
            catch (IOException)
            {
                errors.Add("manifest.json could not be read.");
            }
        }

        if (manifest is not null)
        {
            ValidateManifest(manifest, errors);
        }

        ValidateTransparentPng(packageDirectory, "idle.png", errors);
        ValidateTransparentPng(packageDirectory, "remind.png", errors);

        return new SkinValidationResult(errors.Count == 0, manifest, errors);
    }

    private static void ValidateManifest(SkinManifest manifest, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id)) errors.Add("manifest.id is required.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) errors.Add("manifest.name is required.");
        if (string.IsNullOrWhiteSpace(manifest.Version)) errors.Add("manifest.version is required.");
        if (string.IsNullOrWhiteSpace(manifest.Author)) errors.Add("manifest.author is required.");
        if (string.IsNullOrWhiteSpace(manifest.License)) errors.Add("manifest.license is required.");
        if (manifest.CanvasWidth is < 32 or > 2048) errors.Add("manifest.canvasWidth must be between 32 and 2048.");
        if (manifest.CanvasHeight is < 32 or > 2048) errors.Add("manifest.canvasHeight must be between 32 and 2048.");
        if (manifest.Fps is < 1 or > 30) errors.Add("manifest.fps must be between 1 and 30.");
    }

    private static void ValidateTransparentPng(
        string packageDirectory,
        string fileName,
        List<string> errors)
    {
        var path = Path.Combine(packageDirectory, fileName);
        if (!File.Exists(path))
        {
            errors.Add($"{fileName} is required.");
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            for (var index = 3; index < pixels.Length; index += 4)
            {
                if (pixels[index] < 255) return;
            }

            errors.Add($"{fileName} must contain at least one transparent pixel.");
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            errors.Add($"{fileName} is not a readable PNG with an alpha channel.");
        }
    }

    private static SkinValidationResult Invalid(string error) =>
        new(false, null, [error]);
}
