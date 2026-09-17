using System;
using System.IO;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class ManualReminderPresentationContractTests
{
    [Fact]
    public void ManualFeedbackMustRespectPetVisibilitySetting()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "IkuyoPet.App", "App.xaml.cs"));
        var manualStart = app.IndexOf("viewModel.ManualReminderRequested", StringComparison.Ordinal);
        var manualEnd = app.IndexOf("petWindow.ActionInvoked", manualStart, StringComparison.Ordinal);
        Assert.True(manualStart >= 0 && manualEnd > manualStart);
        var manualHandler = app[manualStart..manualEnd];

        Assert.Contains("var petEnabled = viewModel.PetEnabled", manualHandler, StringComparison.Ordinal);
        Assert.Contains("petEnabled ? \"pet\" : \"notification\"", manualHandler, StringComparison.Ordinal);
        Assert.Contains("if (petEnabled)", manualHandler, StringComparison.Ordinal);
        Assert.Contains("notificationPresenter.ShowAsync", manualHandler, StringComparison.Ordinal);
        Assert.Contains("ManualReminderRequested", manualHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsNotificationsMustNotBeSuppressed()
    {
        var root = FindRepositoryRoot();
        var presenter = File.ReadAllText(Path.Combine(root, "src", "IkuyoPet.Infrastructure", "Windows", "WindowsNotificationPresenter.cs"));

        Assert.DoesNotContain("notification.SuppressDisplay = true", presenter, StringComparison.Ordinal);
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