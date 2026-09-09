using System.IO;
using IkuyoPet.Pet;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetInteractionCatalogLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ikuyopet-interactions-" + Guid.NewGuid());

    public PetInteractionCatalogLoaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task MissingCatalogUsesFallbackWithDiagnostic()
    {
        var fallback = PetInteractionDefaults.Create();
        var loader = new PetInteractionCatalogLoader();

        var result = await loader.LoadAsync(Path.Combine(_directory, "missing.json"), fallback, TestContext.Current.CancellationToken);

        Assert.True(result.UsedFallback);
        Assert.Same(fallback, result.Catalog);
        Assert.NotNull(result.Diagnostic);
    }

    [Fact]
    public async Task InvalidJsonUsesFallbackWithDiagnostic()
    {
        var path = Write("broken.json", "{ not json");
        var fallback = PetInteractionDefaults.Create();
        var loader = new PetInteractionCatalogLoader();

        var result = await loader.LoadAsync(path, fallback, TestContext.Current.CancellationToken);

        Assert.True(result.UsedFallback);
        Assert.Same(fallback, result.Catalog);
        Assert.Contains("Invalid interaction catalog JSON", result.Diagnostic);
    }

    [Theory]
    [InlineData("hover", "zh-CN")]
    [InlineData("click", "en-US")]
    public async Task WrongEventOrLanguageUsesFallback(string interactionEvent, string language)
    {
        var path = Write("wrong-header.json", $$"""
            {
              "character": "default",
              "event": "{{interactionEvent}}",
              "language": "{{language}}",
              "messages": [{ "id": 1, "text": "你好" }]
            }
            """);
        var fallback = PetInteractionDefaults.Create();
        var loader = new PetInteractionCatalogLoader();

        var result = await loader.LoadAsync(path, fallback, TestContext.Current.CancellationToken);

        Assert.True(result.UsedFallback);
        Assert.Same(fallback, result.Catalog);
        Assert.Contains("event=click and language=zh-CN", result.Diagnostic);
    }

    [Fact]
    public async Task FiltersMessagesAndIgnoresUnknownFields()
    {
        var longText = new string('长', 161);
        var path = Write("filtered.json", $$"""
            {
              "character": " default ",
              "event": "click",
              "language": "zh-CN",
              "description": "ignored",
              "canon_sources": ["ignored"],
              "messages": [
                { "id": 0, "text": "bad" },
                { "id": 1, "text": "  第一条  ", "mood": "happy", "unknown": "ignored" },
                { "id": 1, "text": "duplicate" },
                { "id": 2, "text": "" },
                { "id": 3, "text": "{{longText}}" },
                { "id": 4, "text": "第二条", "mood": null }
              ],
              "unknown": "ignored"
            }
            """);
        var loader = new PetInteractionCatalogLoader();

        var result = await loader.LoadAsync(path, PetInteractionDefaults.Create(), TestContext.Current.CancellationToken);

        Assert.False(result.UsedFallback);
        Assert.Null(result.Diagnostic);
        Assert.Equal("default", result.Catalog.Character);
        Assert.Equal("click", result.Catalog.Event);
        Assert.Equal("zh-CN", result.Catalog.Language);
        Assert.Collection(
            result.Catalog.Messages,
            message =>
            {
                Assert.Equal(1, message.Id);
                Assert.Equal("第一条", message.Text);
                Assert.Equal("happy", message.Mood);
            },
            message =>
            {
                Assert.Equal(4, message.Id);
                Assert.Equal("第二条", message.Text);
                Assert.Null(message.Mood);
            });
    }

    [Fact]
    public async Task AllInvalidMessagesUseFallback()
    {
        var path = Write("empty.json", """
            {
              "character": "default",
              "event": "click",
              "language": "zh-CN",
              "messages": [
                { "id": -1, "text": "bad" },
                { "id": 2, "text": "   " }
              ]
            }
            """);
        var fallback = PetInteractionDefaults.Create();
        var loader = new PetInteractionCatalogLoader();

        var result = await loader.LoadAsync(path, fallback, TestContext.Current.CancellationToken);

        Assert.True(result.UsedFallback);
        Assert.Same(fallback, result.Catalog);
        Assert.Contains("no valid interaction messages", result.Diagnostic);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToFallback()
    {
        var path = Write("valid.json", """
            {
              "character": "default",
              "event": "click",
              "language": "zh-CN",
              "messages": [{ "id": 1, "text": "你好" }]
            }
            """);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var loader = new PetInteractionCatalogLoader();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            loader.LoadAsync(path, PetInteractionDefaults.Create(), cts.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string Write(string fileName, string text)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, text);
        return path;
    }
}


