using System.Text.Json;
using IkuyoPet.Core.Skins;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class SkinSelectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string rootDirectory;
    private readonly string selectionPath;

    public SkinSelectionStore(string skinsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skinsRoot);
        rootDirectory = Path.GetFullPath(skinsRoot);
        selectionPath = Path.Combine(rootDirectory, "active-skin.json");
    }

    public async Task SaveAsync(SkinSelection selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ValidateSegment(selection.Id, nameof(selection.Id));
        ValidateSegment(selection.Version, nameof(selection.Version));
        Directory.CreateDirectory(rootDirectory);
        var tempPath = Path.Combine(rootDirectory, $".active-skin.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, selection, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(tempPath, selectionPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async Task<SkinSelection?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(selectionPath)) return null;
        try
        {
            await using var stream = new FileStream(selectionPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var value = await JsonSerializer.DeserializeAsync<SkinSelection>(stream, JsonOptions, cancellationToken);
            if (value is null) return null;
            ValidateSegment(value.Id, nameof(value.Id));
            ValidateSegment(value.Version, nameof(value.Version));
            return value;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static void ValidateSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
            throw new ArgumentException("Skin id/version must be a single safe path segment.", parameterName);
    }
}