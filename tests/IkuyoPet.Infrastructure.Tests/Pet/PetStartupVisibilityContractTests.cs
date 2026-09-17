using IkuyoPet.Infrastructure.Tests.TestSupport;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetStartupVisibilityContractTests
{
    [Fact]
    public void StartupUsesExplicitShowActivationAndMeasuredClamp()
    {
        var app = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "IkuyoPet.App", "App.xaml.cs"));
        var pet = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "IkuyoPet.Pet", "PetWindow.xaml.cs"));
        Assert.Contains("petWindow.ShowAtStartup()", app);
        Assert.Contains("public void ShowAtStartup()", pet);
        Assert.Contains("if (!IsVisible) Show()", pet);
        Assert.Contains("Activate()", pet);
        Assert.Contains("Dispatcher.BeginInvoke", pet);
        Assert.Contains("double.IsFinite(Left)", pet);
        Assert.Contains("double.IsFinite(Top)", pet);
        Assert.Contains("DragMove();", pet);
        Assert.Contains("DragMove();\n        ClampToWorkArea();", pet.Replace("\r\n", "\n"));
    }
}
