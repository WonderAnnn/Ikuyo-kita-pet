using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class WindowsStartupManagerTests
{
    [Fact]
    public void StartupManagerUsesCurrentUserStoreWithoutAdminRights()
    {
        var store = new RecordingStartupStore();
        var manager = new WindowsStartupManager(store, @"G:\Apps\IkuyoPet.exe");

        var result = manager.SetEnabled(true);

        Assert.True(result.Success);
        Assert.Equal(@"G:\Apps\IkuyoPet.exe", store.Value);
        Assert.True(manager.IsEnabled);
    }

    [Fact]
    public void DisablingStartupRemovesCurrentUserValue()
    {
        var store = new RecordingStartupStore { Value = @"G:\Apps\IkuyoPet.exe" };
        var manager = new WindowsStartupManager(store, @"G:\Apps\IkuyoPet.exe");

        var result = manager.SetEnabled(false);

        Assert.True(result.Success);
        Assert.Null(store.Value);
        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public void WritingStartupEntryFailureReturnsFeedbackInsteadOfThrowing()
    {
        var manager = new WindowsStartupManager(new ThrowingStartupStore(), @"G:\Apps\IkuyoPet.exe");

        var result = manager.SetEnabled(true);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ReadingStartupEntryFailureReturnsFeedbackInsteadOfAssumingDisabled()
    {
        var manager = new WindowsStartupManager(new ThrowingStartupStore(), @"G:\Apps\IkuyoPet.exe");

        var result = manager.ReadStatus();

        Assert.False(result.Success);
        Assert.False(result.Enabled);
        Assert.NotNull(result.ErrorMessage);
    }

    private sealed class ThrowingStartupStore : IStartupEntryStore
    {
        public string? Read(string valueName) => throw new InvalidOperationException("registry unavailable");
        public void Write(string valueName, string value) => throw new InvalidOperationException("registry unavailable");
        public void Delete(string valueName) => throw new InvalidOperationException("registry unavailable");
    }

    private sealed class RecordingStartupStore : IStartupEntryStore
    {
        public string? Value { get; set; }
        public string? Read(string valueName) => Value;
        public void Write(string valueName, string value) => Value = value;
        public void Delete(string valueName) => Value = null;
    }
}
