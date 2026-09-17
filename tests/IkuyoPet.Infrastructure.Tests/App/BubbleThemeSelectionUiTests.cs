using System.IO;
using IkuyoPet.Core.Bubbles;
using IkuyoPet.Core.Dashboard;
using IkuyoPet.Infrastructure.Storage;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class BubbleThemeSelectionUiTests
{
    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));

    [Fact]
    public void ViewModelDefaultsToExactlyThreeThemesAndFirstTheme()
    {
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new EmptyDashboard());
        Assert.Equal(3, viewModel.BubbleThemes.Count);
        Assert.Equal(BubbleThemeCatalog.BuiltInThemes[0].Id, viewModel.SelectedBubbleTheme.Id);
        Assert.True(viewModel.BubbleThemes[0].IsSelected);
    }

    [Fact]
    public async Task SelectionPersistsAndRaisesRealtimeChangeEvent()
    {
        using var workspace = new TemporaryThemeWorkspace();
        var catalog = new BubbleThemeCatalog(workspace.AssetsRoot);
        var store = new BubbleThemeSelectionStore(workspace.SelectionRoot);
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new EmptyDashboard());
        viewModel.ConfigureBubbleThemes(catalog, store);
        BubbleThemeDefinition? changedTheme = null;
        viewModel.BubbleThemeChanged += (_, theme) => changedTheme = theme;
        viewModel.SelectBubbleThemeCommand.Execute("cloud-guitar");
        await WaitUntilAsync(async () => await store.LoadAsync(TestContext.Current.CancellationToken) == "cloud-guitar");
        Assert.Equal("cloud-guitar", viewModel.SelectedBubbleTheme.Id);
        Assert.Equal("cloud-guitar", changedTheme?.Id);
        Assert.Contains("元气微笑", viewModel.BubbleThemeStatus);
        var restarted = new IkuyoPet.App.MainWindowViewModel(new EmptyDashboard());
        restarted.ConfigureBubbleThemes(catalog, store);
        await restarted.LoadSettingsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("cloud-guitar", restarted.SelectedBubbleTheme.Id);
    }

    [Fact]
    public async Task CorruptSelectionFallsBackToFirstTheme()
    {
        using var workspace = new TemporaryThemeWorkspace();
        Directory.CreateDirectory(workspace.SelectionRoot);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.SelectionRoot, "bubble-theme.json"),
            "{broken-json",
            TestContext.Current.CancellationToken);
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new EmptyDashboard());
        viewModel.ConfigureBubbleThemes(
            new BubbleThemeCatalog(workspace.AssetsRoot),
            new BubbleThemeSelectionStore(workspace.SelectionRoot));

        await viewModel.LoadSettingsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BubbleThemeCatalog.BuiltInThemes[0].Id, viewModel.SelectedBubbleTheme.Id);
    }

    [Fact]
    public void PetPanelUsesHorizontalThemeCardsWithSelectionAndNoFocusRectangle()
    {
        var view = Read("src/IkuyoPet.App/MainWindow.xaml");
        Assert.Contains("气泡样式", view);
        Assert.Contains("ItemsSource=\"{Binding BubbleThemes}\"", view);
        Assert.Contains("Command=\"{Binding DataContext.SelectBubbleThemeCommand", view);
        Assert.Contains("CommandParameter=\"{Binding Id}\"", view);
        Assert.Contains("Orientation=\"Horizontal\"", view);
        Assert.Contains("Stretch=\"Uniform\"", view);
        Assert.Contains("BubbleThemeCardButton", view);
        Assert.Contains("x:Key=\"BubbleThemeCardButton\"", view);
        Assert.Contains("FocusVisualStyle\" Value=\"{x:Null}\"", view);
        Assert.Contains("Binding IsSelected", view);
        Assert.Contains("xmlns:pet=\"clr-namespace:IkuyoPet.Pet;assembly=IkuyoPet.Pet\"", view);
        Assert.Contains("<pet:BubbleThemePreview", view);
        Assert.Contains("Theme=\"{Binding Theme}\"", view);
        Assert.Contains("ResourcePath=\"{Binding PreviewPath}\"", view);
        Assert.DoesNotContain("<Image Source=\"{Binding PreviewPath}\"", view);
    }

    [Fact]
    public void GuitarAndSmileCardsKeepCopyMatchedToTheirPreviewArtwork()
    {
        var viewModel = new IkuyoPet.App.MainWindowViewModel(new EmptyDashboard());

        var guitar = Assert.Single(viewModel.BubbleThemes, card => card.Name == "吉他现场");
        Assert.Contains("吉他", guitar.Description);
        Assert.Contains("右侧", guitar.Description);
        Assert.EndsWith(
            Path.Combine("cloud-smile", "bubble-filled.png"),
            guitar.PreviewPath.Replace('/', Path.DirectorySeparatorChar));

        var smile = Assert.Single(viewModel.BubbleThemes, card => card.Name == "元气微笑");
        Assert.Contains("元气", smile.Description);
        Assert.Contains("微笑", smile.Description);
        Assert.EndsWith(
            Path.Combine("cloud-guitar", "bubble-filled.png"),
            smile.PreviewPath.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void AppPackagesBubbleAssetsAndReportsVersion014()
    {
        var project = Read("src/IkuyoPet.App/IkuyoPet.App.csproj");
        Assert.Contains("<Version>0.1.4</Version>", project);
        Assert.Contains(@"..\..\assets\bubbles\**\*.*", project);
        Assert.Contains(@"Link=""assets\bubbles\%(RecursiveDir)%(Filename)%(Extension)""", project);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", project);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", project);
    }

    [Fact]
    public void RunningPetPageAndStartupUseTheSameThreeThemeCatalog()
    {
        var view = Read("src/IkuyoPet.App/MainWindow.xaml");
        var windowCode = Read("src/IkuyoPet.App/MainWindow.xaml.cs");
        var startup = Read("src/IkuyoPet.App/App.xaml.cs");

        Assert.Contains("x:Name=\"PetPanel\"", view);
        Assert.Contains("ItemsSource=\"{Binding BubbleThemes}\"", view);
        Assert.Contains("[MainWindowPage.Pet] = PetPanel", windowCode);
        Assert.Contains("viewModel.ConfigureBubbleThemes(bubbleThemeCatalog, bubbleThemeSelectionStore)", startup);
        Assert.Contains("await viewModel.LoadSettingsAsync", startup);
        Assert.Contains("petWindow.SetBubbleTheme(", startup);
        Assert.Contains("viewModel.BubbleThemeChanged +=", startup);
    }

    [Fact]
    public void ThemeSelectionDoesNotSynchronouslyWaitForPersistence()
    {
        var viewModel = Read("src/IkuyoPet.App/MainWindowViewModel.cs");
        var selectionStart = viewModel.IndexOf("private async Task SelectBubbleThemeAsync", StringComparison.Ordinal);
        var selectionEnd = viewModel.IndexOf("private void ApplyBubbleTheme", selectionStart, StringComparison.Ordinal);

        Assert.True(selectionStart >= 0 && selectionEnd > selectionStart);
        Assert.DoesNotContain(
            "GetAwaiter().GetResult()",
            viewModel[selectionStart..selectionEnd]);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Fail("The asynchronous selection was not persisted in time.");
    }

    private sealed class EmptyDashboard : IDashboardQueryService
    {
        public Task<DashboardSnapshot> GetAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardSnapshot(day, [], 0, 0, 0, 0, TimeSpan.Zero));
    }

    private sealed class TemporaryThemeWorkspace : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"ikuyo-bubble-ui-{Guid.NewGuid():N}");
        public TemporaryThemeWorkspace()
        {
            foreach (var theme in BubbleThemeCatalog.BuiltInThemes)
            {
                var path = Path.Combine(AssetsRoot, theme.ResourcePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
            }
        }
        public string AssetsRoot => Path.Combine(root, "assets");
        public string SelectionRoot => Path.Combine(root, "selection");
        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
