using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using IkuyoPet.Infrastructure.Tests.TestSupport;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class PrivateAssetContractTests
{
    [Fact]
    public void GitignoreKeepsPrivateAndGeneratedRootsOutOfGit()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root, ".gitignore"));
        Assert.Contains(lines, line => line.Trim() == "local-assets/");
        Assert.Contains(lines, line => line.Trim() == "local-skins/");
        Assert.Contains(lines, line => line.Trim() == "tmp/");
        Assert.Contains(lines, line => line.Trim() == "output/");
        Assert.Contains(lines, line => line.Trim() == "assets/skins/private/");
        Assert.Contains(lines, line => line.Trim() == "assets/skins/third-party/");
        Assert.DoesNotContain(lines, line => line.Trim() == "assets/");
    }

    [Fact]
    public void AppProjectUsesPublicBrandingIcon()
    {
        var projectPath = Path.Combine(RepositoryPaths.Root, "src", "IkuyoPet.App", "IkuyoPet.App.csproj");
        var project = XDocument.Load(projectPath);
        XElement Property(string name) => project.Descendants(name).Single();

        Assert.Equal("'$(LocalAssetsRoot)' == ''", Property("LocalAssetsRoot").Attribute("Condition")?.Value);
        Assert.Equal(@"$(MSBuildProjectDirectory)\..\..\local-assets", Property("LocalAssetsRoot").Value);
        Assert.Equal(@"$(MSBuildProjectDirectory)\..\..\assets\branding\icon\icon256.ico", Property("PublicBrandingIcon").Value);
        Assert.Equal("Exists('$(PublicBrandingIcon)')", Property("ApplicationIcon").Attribute("Condition")?.Value);
        Assert.Equal("$(PublicBrandingIcon)", Property("ApplicationIcon").Value);
        Assert.Equal("'$(IncludeLocalOverrides)' == ''", Property("IncludeLocalOverrides").Attribute("Condition")?.Value);
    }

    [Fact]
    public void AppProjectIncludesPublicSkinAndInteractionAssets()
    {
        var projectPath = Path.Combine(RepositoryPaths.Root, "src", "IkuyoPet.App", "IkuyoPet.App.csproj");
        var project = XDocument.Load(projectPath);

        var skin = project.Descendants("Content").Single(element =>
            element.Attribute("Link")?.Value == @"skins\%(RecursiveDir)%(Filename)%(Extension)");
        Assert.Equal(@"..\..\assets\skins\**\*.*", skin.Attribute("Include")?.Value);
        Assert.Equal("PreserveNewest", skin.Attribute("CopyToOutputDirectory")?.Value);
        Assert.Equal("PreserveNewest", skin.Attribute("CopyToPublishDirectory")?.Value);

        var interaction = project.Descendants("Content").Single(element =>
            element.Attribute("Link")?.Value == @"interactions\kita-click.json");
        Assert.Equal(@"..\..\assets\interactions\kita-click.zh-CN.json", interaction.Attribute("Include")?.Value);
        Assert.Equal("PreserveNewest", interaction.Attribute("CopyToPublishDirectory")?.Value);
    }

    [Fact]
    public void AppProjectIncludesPrivateInteractionJsonOnlyAsLocalOverride()
    {
        var projectPath = Path.Combine(RepositoryPaths.Root, "src", "IkuyoPet.App", "IkuyoPet.App.csproj");
        var project = XDocument.Load(projectPath);
        var content = project.Descendants("Content").Single(element =>
            element.Attribute("Link")?.Value == @"interactions\ikuyo-click.json");

        Assert.Contains("Exists('$(LocalAssetsRoot)\\interactions\\ikuyo-click.zh-CN.json')", content.Attribute("Condition")?.Value);
        Assert.Contains("'$(IncludeLocalOverrides)' == 'true'", content.Attribute("Condition")?.Value);
        Assert.Equal(@"$(LocalAssetsRoot)\interactions\ikuyo-click.zh-CN.json", content.Attribute("Include")?.Value);
        Assert.Equal("PreserveNewest", content.Attribute("CopyToOutputDirectory")?.Value);
        Assert.Equal("PreserveNewest", content.Attribute("CopyToPublishDirectory")?.Value);
    }

    [Fact]
    public void VerifyDevChecksPublicAssetsAndRejectsPrivatePublishContent()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "eng", "verify-dev.ps1"));

        Assert.Contains("ls-files -- 'local-skins/**' 'local-assets/**'", script);
        Assert.Contains("PublicSourceFiles", script);
        Assert.Contains("PublicPublishAssetsPresent", script);
        Assert.Contains("PublishHasPrivateOverrides", script);
        Assert.Contains("Generated files must not be tracked by Git", script);
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