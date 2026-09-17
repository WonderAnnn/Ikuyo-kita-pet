using System.IO;
using IkuyoPet.Infrastructure.Tests.TestSupport;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class AppCompositionContractTests
{
    [Fact]
    public void AppLoadsFallbackPublicAndOptionalLocalInteractionCatalogs()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "src", "IkuyoPet.App", "App.xaml.cs"));

        Assert.Contains("\"default\"", source);
        Assert.Contains("\"click.json\"", source);
        Assert.Contains("\"kita-click.json\"", source);
        Assert.Contains("\"ikuyo-click.json\"", source);
        Assert.DoesNotContain("click.zh-CN.json", source);
        Assert.Contains("fallbackResult.Catalog", source);
        Assert.Contains("publicResult.Catalog", source);
        Assert.Contains("OnPetInteractionRequested", source);
        Assert.Contains("InteractionRequested += OnPetInteractionRequested", source);
        Assert.Contains("InteractionRequested -= OnPetInteractionRequested", source);
        Assert.Contains("TimeSpan.FromSeconds(4)", source);
    }

    [Fact]
    public void PublishScriptsRequirePublicResourcesAndExcludePrivateOverrides()
    {
        var localScript = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "eng", "publish-local.ps1"));
        var latestScript = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "scripts", "publish-latest.ps1"));

        Assert.Contains(@"interactions\default\click.json", localScript);
        Assert.Contains(@"interactions\kita-click.json", localScript);
        Assert.Contains("IncludeLocalOverrides=false", localScript);
        Assert.Contains("Publish output contains private local overrides", localScript);
        Assert.Contains("verify-dev.ps1", localScript);

        Assert.Contains("latest-staging-", latestScript);
        Assert.Contains("build-info.json", latestScript);
        Assert.Contains("resourceCount", latestScript);
        Assert.Contains("$assetRoot", latestScript);
        Assert.Contains("$sourceFiles = Get-ChildItem", latestScript);
        Assert.Contains("sourceHash", latestScript);
        Assert.Contains("publishedHash", latestScript);
        Assert.Contains("verify-dev.ps1", latestScript);
        Assert.Contains("Get-FileHash", latestScript);
        Assert.Contains("IncludeLocalOverrides=false", latestScript);
        Assert.Contains("Move-Item -LiteralPath $staging -Destination $latest", latestScript);
        Assert.Contains("Move-Item -LiteralPath $backup -Destination $latest", latestScript);
    }
}