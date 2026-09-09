using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;

namespace IkuyoPet.Pet;

public sealed record PetInteractionMessage(int Id, string Text, string? Mood);

public sealed record PetInteractionCatalog(
    string Character,
    string Event,
    string Language,
    IReadOnlyList<PetInteractionMessage> Messages);

public sealed record PetInteractionLoadResult(
    PetInteractionCatalog Catalog,
    bool UsedFallback,
    string? Diagnostic);

public static class PetInteractionDefaults
{
    public static PetInteractionCatalog Create() => new(
        "default",
        "click",
        "zh-CN",
        new[]
        {
            new PetInteractionMessage(1, "今天也一起按自己的节奏前进吧～", null),
            new PetInteractionMessage(2, "先完成眼前的一小步，就已经很棒啦！", null),
            new PetInteractionMessage(3, "累了就放松一下肩膀，再继续也不迟。", null),
        });
}

public class PetInteractionCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Loader is an instance service for future composition.")]
    public async Task<PetInteractionLoadResult> LoadAsync(
        string path,
        PetInteractionCatalog fallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fallback);

        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var source = document.RootElement.Deserialize<RawCatalog>(JsonOptions)
                ?? throw new JsonException("Catalog is null.");

            if (!string.Equals(source.Event, "click", StringComparison.Ordinal) ||
                !string.Equals(source.Language, "zh-CN", StringComparison.Ordinal))
            {
                return Fallback(fallback, "Catalog header must specify event=click and language=zh-CN.");
            }

            var valid = new List<PetInteractionMessage>();
            var ids = new HashSet<int>();
            foreach (var message in source.Messages ?? [])
            {
                var text = message.Text?.Trim();
                if (message.Id <= 0 || !ids.Add(message.Id) || string.IsNullOrWhiteSpace(text) || text.Length > 160)
                    continue;

                valid.Add(new PetInteractionMessage(message.Id, text, message.Mood));
            }

            if (valid.Count == 0)
                return Fallback(fallback, "Catalog contained no valid interaction messages.");

            return new PetInteractionLoadResult(
                new PetInteractionCatalog(source.Character?.Trim() ?? string.Empty, source.Event!, source.Language!, valid),
                false,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            return Fallback(fallback, $"Unable to read interaction catalog: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fallback(fallback, $"Unable to access interaction catalog: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return Fallback(fallback, $"Invalid interaction catalog JSON: {ex.Message}");
        }
    }

    private static PetInteractionLoadResult Fallback(PetInteractionCatalog fallback, string diagnostic) =>
        new(fallback, true, diagnostic);

    private sealed class RawCatalog
    {
        public string? Character { get; set; }
        public string? Event { get; set; }
        public string? Language { get; set; }
        public List<RawMessage>? Messages { get; set; }
    }

    private sealed class RawMessage
    {
        public int Id { get; set; }
        public string? Text { get; set; }
        public string? Mood { get; set; }
    }
}

public sealed class CatalogLoader : PetInteractionCatalogLoader;


