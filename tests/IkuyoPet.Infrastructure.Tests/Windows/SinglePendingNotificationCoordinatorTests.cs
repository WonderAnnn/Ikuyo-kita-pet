using IkuyoPet.Infrastructure.Windows;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Windows;

public sealed class SinglePendingNotificationCoordinatorTests
{
    [Fact]
    public async Task ReplaceAsyncClearsBeforeShowingLatestRequest()
    {
        var transport = new RecordingTransport();
        var coordinator = new SinglePendingNotificationCoordinator(transport);
        var first = TestRequest("first");
        var second = TestRequest("second");

        await coordinator.ReplaceAsync(first, CancellationToken.None);
        await coordinator.ReplaceAsync(second, CancellationToken.None);

        Assert.Equal(
            ["clear", "show:first", "clear", "show:second"],
            transport.Events);
        Assert.Equal(second, transport.LastRequest);
    }

    [Fact]
    public async Task ConcurrentReplacementsNeverOverlap()
    {
        var transport = new RecordingTransport(delay: TimeSpan.FromMilliseconds(5));
        var coordinator = new SinglePendingNotificationCoordinator(transport);

        await Task.WhenAll(
            coordinator.ReplaceAsync(TestRequest("a"), CancellationToken.None),
            coordinator.ReplaceAsync(TestRequest("b"), CancellationToken.None),
            coordinator.ReplaceAsync(TestRequest("c"), CancellationToken.None));

        Assert.Equal(0, transport.OverlappingCalls);
        Assert.Equal(6, transport.Events.Count);
        Assert.Matches("^show:[abc]$", transport.Events[^1]);
    }

    [Fact]
    public async Task ClearAsyncIsIdempotent()
    {
        var transport = new RecordingTransport();
        var coordinator = new SinglePendingNotificationCoordinator(transport);

        await coordinator.ClearAsync(CancellationToken.None);
        await coordinator.ClearAsync(CancellationToken.None);

        Assert.Equal(["clear", "clear"], transport.Events);
    }

    private static NotificationRequest TestRequest(string text) => new(
        Guid.NewGuid(), "Ikuyo Pet", text, []);

    private sealed class RecordingTransport(TimeSpan? delay = null)
        : IPendingNotificationTransport
    {
        private int activeCalls;
        private int overlappingCalls;

        public List<string> Events { get; } = [];
        public NotificationRequest? LastRequest { get; private set; }
        public int OverlappingCalls => overlappingCalls;

        public async Task ClearAsync(CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            try { Events.Add("clear"); }
            finally { Exit(); }
        }

        public async Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            try
            {
                Events.Add($"show:{request.Message}");
                LastRequest = request;
            }
            finally { Exit(); }
        }

        private async Task EnterAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref activeCalls) > 1)
            {
                Interlocked.Increment(ref overlappingCalls);
            }

            if (delay is { } value)
            {
                await Task.Delay(value, cancellationToken);
            }
        }

        private void Exit() => Interlocked.Decrement(ref activeCalls);
    }
}
