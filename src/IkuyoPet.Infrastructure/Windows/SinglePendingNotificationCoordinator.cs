namespace IkuyoPet.Infrastructure.Windows;

public interface IPendingNotificationTransport
{
    Task ClearAsync(CancellationToken cancellationToken);
    Task ShowAsync(NotificationRequest request, CancellationToken cancellationToken);
}

public sealed class SinglePendingNotificationCoordinator : IDisposable
{
    private readonly IPendingNotificationTransport transport;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public SinglePendingNotificationCoordinator(IPendingNotificationTransport transport)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task ReplaceAsync(
        NotificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(disposed, this);

        await gate.WaitAsync(cancellationToken);
        try
        {
            await transport.ClearAsync(cancellationToken);
            await transport.ShowAsync(request, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await gate.WaitAsync(cancellationToken);
        try
        {
            await transport.ClearAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        gate.Dispose();
    }
}
