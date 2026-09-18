using IkuyoPet.Uninstaller;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Uninstaller;

public sealed class UninstallTargetValidatorTests
{
    [Fact]
    public void AcceptsOnlyDirectoryWithPublishedMarkers()
    {
        using var fixture = PublishedDirectoryFixture.Create();

        var result = UninstallTargetValidator.Validate(fixture.Root, fixture.DataRoot);

        Assert.True(result.IsValid);
        Assert.Equal(fixture.Root, result.InstallRoot);
    }

    [Fact]
    public void RejectsRepositoryRootAndMissingBuildInfo()
    {
        using var fixture = PublishedDirectoryFixture.Create(withBuildInfo: false);

        Assert.False(UninstallTargetValidator.Validate(fixture.Root, fixture.DataRoot).IsValid);
        Assert.False(
            UninstallTargetValidator.Validate(
                Directory.GetParent(fixture.Root)!.FullName,
                fixture.DataRoot).IsValid);
    }

    [Fact]
    public void PreservePlanLeavesDataUntouched()
    {
        using var fixture = PublishedDirectoryFixture.Create();
        var validation = UninstallTargetValidator.Validate(fixture.Root, fixture.DataRoot);

        var plan = UninstallPlan.Create(validation, UninstallDataChoice.Preserve);

        Assert.Null(plan.DataRootToDelete);
    }

    [Fact]
    public void DeleteDataPlanTargetsOnlyLocalAppDataDirectory()
    {
        using var fixture = PublishedDirectoryFixture.Create();
        var validation = UninstallTargetValidator.Validate(fixture.Root, fixture.DataRoot);

        var plan = UninstallPlan.Create(validation, UninstallDataChoice.Delete);

        Assert.Equal(
            Path.GetFullPath(fixture.DataRoot),
            Path.GetFullPath(plan.DataRootToDelete!));
        Assert.EndsWith(
            "IkuyoPet",
            plan.DataRootToDelete!,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PublishedDirectoryFixture : IDisposable
    {
        private PublishedDirectoryFixture(string root, string dataRoot, string id)
        {
            Root = root;
            DataRoot = dataRoot;
            Id = id;
        }

        public string Root { get; }
        public string DataRoot { get; }
        public string Id { get; }

        public static PublishedDirectoryFixture Create(bool withBuildInfo = true)
        {
            var id = Guid.NewGuid().ToString("N");
            var root = Path.Combine(Path.GetTempPath(), "IkuyoPet-UninstallTests", id, "latest");
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IkuyoPet");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "IkuyoPet.exe"), "test executable");
            if (withBuildInfo)
            {
                File.WriteAllText(Path.Combine(root, "build-info.json"), "{}\n");
            }

            return new PublishedDirectoryFixture(root, dataRoot, id);
        }

        public void Dispose()
        {
            var installParent = Directory.GetParent(Root)?.Parent?.FullName;
            if (!string.IsNullOrWhiteSpace(installParent) && Directory.Exists(installParent))
            {
                Directory.Delete(installParent, recursive: true);
            }

        }
    }
}
