using System.IO;
using IkuyoPet.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Storage;

public sealed class DatabaseMigratorConcurrencyTests
{
    [Fact]
    public async Task FailedMigrationSharedByTwoWaitersAllowsOneReplacementRun()
    {
        var connectionString = UniqueFileConnectionString();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var failing = new DatabaseMigrator(connectionString, async () =>
        {
            Interlocked.Increment(ref executions);
            started.TrySetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("expected migration failure");
        });
        var first = failing.MigrateAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = failing.MigrateAsync(TestContext.Current.CancellationToken);

        release.TrySetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        var replacement = new DatabaseMigrator(connectionString, () =>
        {
            Interlocked.Increment(ref executions);
            return Task.CompletedTask;
        });
        await replacement.MigrateAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        await replacement.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task EquivalentConnectionStringsShareOneInProcessMigration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ikuyo-pet-{Guid.NewGuid():N}.db");
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, path);
        var firstConnectionString =
            $"Data Source={relativePath};Mode=ReadWriteCreate;Cache=Shared";
        var secondConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        var thirdConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(path).AbsoluteUri,
        }.ToString();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var first = new DatabaseMigrator(firstConnectionString, async () =>
        {
            Interlocked.Increment(ref executions);
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        });
        var second = new DatabaseMigrator(secondConnectionString, () =>
        {
            Interlocked.Increment(ref executions);
            return Task.CompletedTask;
        });
        var third = new DatabaseMigrator(thirdConnectionString, () =>
        {
            Interlocked.Increment(ref executions);
            return Task.CompletedTask;
        });

        var firstTask = first.MigrateAsync(TestContext.Current.CancellationToken);
        var secondTask = second.MigrateAsync(TestContext.Current.CancellationToken);
        var thirdTask = third.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, executions);
        release.TrySetResult();
        await Task.WhenAll(firstTask, secondTask, thirdTask);
    }

    [Fact]
    public async Task CanceledOnlyWaiterDoesNotLeaveBackgroundFailureCached()
    {
        var connectionString = UniqueFileConnectionString();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource();
        var executions = 0;
        var failing = new DatabaseMigrator(connectionString, async () =>
        {
            Interlocked.Increment(ref executions);
            started.TrySetResult();
            await failure.Task;
        });
        using var cancellation = new CancellationTokenSource();
        var canceledWaiter = failing.MigrateAsync(cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWaiter);
        failure.SetException(new InvalidOperationException("background failure"));
        await Task.Yield();

        var replacement = new DatabaseMigrator(connectionString, () =>
        {
            Interlocked.Increment(ref executions);
            return Task.CompletedTask;
        });
        await replacement.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, executions);
    }

    private static string UniqueFileConnectionString()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ikuyo-pet-gate-{Guid.NewGuid():N}.db");
        return new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }
}
