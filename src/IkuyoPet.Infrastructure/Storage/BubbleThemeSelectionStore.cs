using System.Text.Json;

namespace IkuyoPet.Infrastructure.Storage;

public sealed class BubbleThemeSelectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> KnownIds = new(StringComparer.OrdinalIgnoreCase)
        { "cloud-chibi", "cloud-guitar", "cloud-smile" };
    private readonly string rootDirectory;
    private readonly string selectionPath;

    public BubbleThemeSelectionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        selectionPath = Path.Combine(this.rootDirectory, "bubble-theme.json");
    }

    public async Task SaveAsync(string themeId, CancellationToken cancellationToken = default)
    {
        ValidateId(themeId);
        Directory.CreateDirectory(rootDirectory);
        var tempPath = Path.Combine(rootDirectory, $".bubble-theme.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, new Selection(themeId), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(tempPath, selectionPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(selectionPath)) return null;
        try
        {
            await using var stream = new FileStream(selectionPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var selection = await JsonSerializer.DeserializeAsync<Selection>(stream, JsonOptions, cancellationToken);
            return selection is not null && KnownIds.Contains(selection.Id) ? selection.Id : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains('/') || id.Contains('\\') || !KnownIds.Contains(id))
            throw new ArgumentException("Bubble theme id must be one of the built-in safe ids.", nameof(id));
    }

    private sealed record Selection(string Id);
}
