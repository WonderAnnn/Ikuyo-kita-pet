using System;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using IkuyoPet.Infrastructure.Tests.TestSupport;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class PrivateAssetContractTests
{
    [Fact]
    public void GitignoreIgnoresLocalAssetsAsAnIndependentRule()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root, ".gitignore"));
        Assert.Contains(lines, line => line.Trim() == "local-assets/");
    }

    [Fact]
    public void AppProjectUsesPrivateBrandingIconOnlyWhenItExists()
    {
        var projectPath = Path.Combine(RepositoryPaths.Root, "src", "IkuyoPet.App", "IkuyoPet.App.csproj");
        var project = XDocument.Load(projectPath);
        XElement Property(string name) => project.Descendants(name).Single();

        Assert.Equal("'$(LocalAssetsRoot)' == ''", Property("LocalAssetsRoot").Attribute("Condition")?.Value);
        Assert.Equal(@"$(MSBuildProjectDirectory)\..\..\local-assets", Property("LocalAssetsRoot").Value);
        Assert.Equal(@"$(LocalAssetsRoot)\branding\icon\icon256.ico", Property("LocalBrandingIcon").Value);
        Assert.Equal("Exists('$(LocalBrandingIcon)')", Property("ApplicationIcon").Attribute("Condition")?.Value);
        Assert.Equal("$(LocalBrandingIcon)", Property("ApplicationIcon").Value);
    }

    [Fact]
    public void AppProjectIncludesPrivateInteractionJsonConditionally()
    {
        var projectPath = Path.Combine(RepositoryPaths.Root, "src", "IkuyoPet.App", "IkuyoPet.App.csproj");
        var project = XDocument.Load(projectPath);
        var content = project.Descendants("Content").Single(element =>
            element.Attribute("Link")?.Value == @"interactions\ikuyo-click.zh-CN.json");

        Assert.Equal("Exists('$(LocalAssetsRoot)\\interactions\\ikuyo-click.zh-CN.json')", content.Attribute("Condition")?.Value);
        Assert.Equal(@"$(LocalAssetsRoot)\interactions\ikuyo-click.zh-CN.json", content.Attribute("Include")?.Value);
        Assert.Equal("PreserveNewest", content.Attribute("CopyToOutputDirectory")?.Value);
        Assert.Equal("PreserveNewest", content.Attribute("CopyToPublishDirectory")?.Value);
    }

    [Fact]
    public void VerifyDevChecksBothPrivateAssetRootsAndReportsAssetState()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "eng", "verify-dev.ps1"));

        Assert.Contains("ls-files -- 'local-skins' 'local-assets'", script);
        Assert.Contains("PrivateAssetsTracked = $false", script);
        Assert.Contains("PrivateBrandingPresent", script);
        Assert.Contains("PrivateInteractionPresent", script);
    }

    [Fact]
    public void PrivateInteractionJsonHasExpectedShapeWhenPresent()
    {
        var path = Path.Combine(RepositoryPaths.Root, "local-assets", "interactions", "ikuyo-click.zh-CN.json");
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var messages = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToArray()
            : document.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(105, messages.Length);
    }
}
