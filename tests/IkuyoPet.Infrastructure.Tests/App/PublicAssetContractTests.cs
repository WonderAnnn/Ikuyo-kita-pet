using System;
using System.IO;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class PublicAssetContractTests
{
    [Fact]
    public void PublicAssetsAreKeptSeparateFromPrivateOverrides()
    {
        var root = FindRepositoryRoot();
        var lines = File.ReadAllLines(Path.Combine(root, ".gitignore"));

        Assert.Contains(lines, line => line.Trim().Equals("local-assets/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Trim().Equals("local-skins/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Trim().Equals("tmp/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Trim().Equals("output/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Trim().Equals("assets/skins/private/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Trim().Equals("assets/skins/third-party/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Trim().Equals("assets/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublicAssetTreeContainsCompletePublishedResourceSet()
    {
        var root = FindRepositoryRoot();

        var required = new[]
        {
            Path.Combine("assets", "interactions", "kita-click.zh-CN.json"),
            Path.Combine("assets", "branding", "icon", "icon256.ico"),
            Path.Combine("assets", "skins", "kita-original", "1.0.0", "manifest.json"),
            Path.Combine("assets", "skins", "kita-original", "1.0.0", "idle.png"),
            Path.Combine("assets", "skins", "kita-original", "1.0.0", "remind.png"),
            Path.Combine("assets", "bubbles", "kita", "kita_cloud2-source.png"),
            Path.Combine("assets", "bubbles", "kita", "kita_cloud3-source.png"),
        };

        foreach (var relative in required)
            Assert.True(File.Exists(Path.Combine(root, relative)), $"Missing public asset: {relative}");
    }

    [Fact]
    public void PublicAssetsAreDocumentedWithIndependentNotice()
    {
        var root = FindRepositoryRoot();

        var skinNotice = File.ReadAllText(Path.Combine(root, "assets", "skins", "kita-original", "NOTICE.md"));
        var bubbleNotice = File.ReadAllText(Path.Combine(root, "assets", "bubbles", "NOTICE.md"));

        Assert.Contains("授权", skinNotice);
        Assert.Contains("代码许可证", skinNotice);
        Assert.Contains("非官方", skinNotice);
        Assert.Contains("授权", bubbleNotice);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "IkuyoPet.App")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}