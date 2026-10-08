using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class ImageTransferSingleFlightLifecycleTests
{
    [Fact]
    public async Task AlreadyCanceledRequest_DoesNotStartWriter_AndNextRequestCanExecute()
    {
        var service = new ImageTransferSingleFlight();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var executions = 0;
        Task<int> Transfer(CancellationToken _)
        {
            executions++;
            return Task.FromResult(7);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync("image", Transfer, canceled.Token));
        Assert.Equal(0, executions);
        Assert.Equal(7, await service.RunAsync("image", Transfer, default));
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task SoleWaiterCancellation_WriterFinishes_DeletedCacheIsPreparedAgain()
    {
        var service = new ImageTransferSingleFlight();
        using var waiter = new CancellationTokenSource();
        // The inner operation finishing does not guarantee the shared task's finally has
        // run under thread-pool pressure. Capture and await the actual shared task below.
        var writer = new TaskCompletionSource<int>();
        var executions = 0;
        var path = Path.Combine(Path.GetTempPath(), $"singleflight-{Guid.NewGuid():N}.cache");
        try
        {
            var first = service.RunAsync("image", token =>
            {
                Assert.False(token.CanBeCanceled);
                executions++;
                File.WriteAllText(path, "first");
                return writer.Task;
            }, waiter.Token);
            var entries = (ConcurrentDictionary<string, Lazy<Task<object?>>>)typeof(ImageTransferSingleFlight)
                .GetField("_operations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
            // Await the already-started shared Task itself. This is not another RunAsync
            // waiter, so it cannot conceal the original waiter-finally cleanup defect.
            var sharedWriter = entries["image"].Value;
            waiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.False(writer.Task.IsCompleted);
            writer.SetResult(1);
            await sharedWriter;
            File.Delete(path);

            var next = await service.RunAsync("image", _ =>
            {
                executions++;
                File.WriteAllText(path, "second");
                return Task.FromResult(2);
            }, default);

            Assert.Equal(2, next);
            Assert.Equal(2, executions);
            Assert.Equal("second", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task MultipleWaiters_ShareLiveWriter_ThenNextRequestExecutesAgain()
    {
        var service = new ImageTransferSingleFlight();
        var writer = new TaskCompletionSource<int>();
        var executions = 0;
        Task<int> Transfer(CancellationToken _)
        {
            executions++;
            return writer.Task;
        }
        var waiters = Enumerable.Range(0, 20).Select(_ => service.RunAsync("image", Transfer, default)).ToArray();
        Assert.Equal(1, executions);
        writer.SetResult(42);
        Assert.All(await Task.WhenAll(waiters), value => Assert.Equal(42, value));

        Assert.Equal(7, await service.RunAsync("image", _ =>
        {
            executions++;
            return Task.FromResult(7);
        }, default));
        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task CanceledWaiter_DoesNotStopWriterOrRemoveItsLiveSlot()
    {
        var service = new ImageTransferSingleFlight();
        var writer = new TaskCompletionSource<int>();
        using var canceled = new CancellationTokenSource();
        var first = service.RunAsync("image", token =>
        {
            Assert.False(token.CanBeCanceled);
            return writer.Task;
        }, canceled.Token);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var alternateStarted = false;
        var next = service.RunAsync("image", _ =>
        {
            alternateStarted = true;
            return Task.FromResult(99);
        }, default);
        Assert.False(alternateStarted);
        Assert.False(next.IsCompleted);
        writer.SetResult(8);
        Assert.Equal(8, await next);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultedWriter_RemovesItsSlotBeforeTheNextAttempt(bool synchronousFailure)
    {
        var service = new ImageTransferSingleFlight();
        var writer = new TaskCompletionSource<int>();
        var first = service.RunAsync<int>("image", _ => synchronousFailure
            ? throw new IOException("transfer failed") : writer.Task, default);
        if (!synchronousFailure) writer.SetException(new IOException("transfer failed"));
        await Assert.ThrowsAsync<IOException>(() => first);
        Assert.Equal(2, await service.RunAsync("image", _ => Task.FromResult(2), default));
    }

    [Fact]
    public async Task OldWriterCompletion_DoesNotRemoveAReplacementWithTheSameKey()
    {
        var service = new ImageTransferSingleFlight();
        var oldWriter = new TaskCompletionSource<int>();
        var first = service.RunAsync("image", _ => oldWriter.Task, default);
        var replacementWriter = new TaskCompletionSource<object?>();
        var replacement = new Lazy<Task<object?>>(() => replacementWriter.Task);
        // Inject the concurrent replacement to exercise the owner-identity invariant:
        // cleanup from a detached writer must never erase another writer's slot.
        var entries = (ConcurrentDictionary<string, Lazy<Task<object?>>>)typeof(ImageTransferSingleFlight)
            .GetField("_operations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
        entries["image"] = replacement;
        oldWriter.SetResult(1);
        Assert.Equal(1, await first);
        var alternateStarted = false;
        var next = service.RunAsync("image", _ =>
        {
            alternateStarted = true;
            return Task.FromResult(99);
        }, default);
        Assert.Same(replacement, entries["image"]);
        Assert.False(alternateStarted);
        replacementWriter.SetResult(3);
        Assert.Equal(3, await next);
    }
}
