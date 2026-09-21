using System;
using System.IO;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class AuthorAttributionContractTests
{
    [Fact]
    public void SettingsViewShowsAuthorAttributionAtTheBottom()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "IkuyoPet.App", "Views", "SettingsView.xaml"));

        Assert.Contains("作者：成都信息工程大学 WonderAnn", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmeShowsAuthorAndAllWindowsNotificationImages()
    {
        var root = RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("作者：成都信息工程大学 WonderAnn", readme, StringComparison.Ordinal);
        foreach (var image in new[] { "win通知喝水.png", "win通知休息.png", "windows通知休息.png" })
        {
            Assert.Contains(image, readme, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, "docs", "assets", "kita", image)), $"Missing README asset: {image}");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "IkuyoPet.App")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
