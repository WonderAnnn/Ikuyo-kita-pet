using IkuyoPet.Infrastructure.Tests.TestSupport;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class PublishLatestContractTests
{
    [Fact]
    public void LatestPublisherUsesValidatedStagingBuildInfoAndAtomicDirectoryMoves()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "scripts", "publish-latest.ps1"));
        Assert.Contains("latest-staging-", script);
        Assert.Contains("build-info.json", script);
        Assert.Contains("$assetRoot", script);
        Assert.Contains("$uninstallerProject", script);
        Assert.Contains("$uninstallerStaging", script);
        Assert.Contains("Get-ChildItem -LiteralPath $uninstallerStaging", script);
        Assert.Contains("IkuyoPet.Uninstaller.exe", script);
        Assert.Contains("uninstaller = [ordered]@", script);
        Assert.Contains("$sourceFiles = Get-ChildItem", script);
        Assert.Contains("sourceHash", script);
        Assert.Contains("publishedHash", script);
        Assert.Contains("resourceCount", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("IncludeLocalOverrides=false", script);
        Assert.Contains("SkipDesktopShortcut", script);
        Assert.Contains("Update-DesktopShortcut", script);
        Assert.Contains("verify-dev.ps1", script);
        Assert.Contains("Move-Item -LiteralPath $staging -Destination $latest", script);
        Assert.Contains("Move-Item -LiteralPath $backup -Destination $latest", script);
        Assert.Contains("Close any running IkuyoPet.exe", script);
    }
}
