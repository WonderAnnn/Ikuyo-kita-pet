using System.IO;
using IkuyoPet.Infrastructure.Tests.TestSupport;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class AppCompositionContractTests
{
    [Fact]
    public void AppLoadsSafeLinkedInteractionCatalogsAndUnsubscribesNamedHandler()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.App", "App.xaml.cs"));

        Assert.Contains("\"default\"", source);
        Assert.Contains("\"click.json\"", source);
        Assert.Contains("\"ikuyo-click.json\"", source);
        Assert.DoesNotContain("click.zh-CN.json", source);
        Assert.Contains("OnPetInteractionRequested", source);
        Assert.Contains("InteractionRequested += OnPetInteractionRequested", source);
        Assert.Contains("InteractionRequested -= OnPetInteractionRequested", source);
        Assert.Contains("TimeSpan.FromSeconds(4)", source);
    }

    [Fact]
    public void PublishScriptRequiresPublicFallbackAndTreatsPrivateCatalogAsOptional()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "eng", "publish-local.ps1"));

        Assert.Contains(@"interactions\default\click.json", source);
        Assert.Contains(@"interactions\ikuyo-click.json", source);
        Assert.Contains("Private interaction source exists but publish output is missing", source);
        Assert.Contains("verify-dev.ps1", source);
    }
}
