using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class NodeDispatchBudgetTests
{
    [Fact]
    public async Task SameNodeAndCategory_NeverExceedsBudget()
    {
        var limiter = new NodeDispatchLimiter();
        var nodeId = Guid.NewGuid();
        var active = 0;
        var maximum = 0;

        var tasks = Enumerable.Range(0, 12).Select(_ => limiter.RunAsync(
            nodeId,
            NodeDispatchCategory.DockerCreate,
            2,
            async token =>
            {
                var current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximum, current);
                try
                {
                    await Task.Delay(20, token);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            },
            CancellationToken.None));

        await Task.WhenAll(tasks);

        Assert.Equal(2, maximum);
    }

    [Fact]
    public async Task SeparateNodes_CanDispatchAtTheSameTime()
    {
        var limiter = new NodeDispatchLimiter();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = limiter.RunAsync(Guid.NewGuid(), NodeDispatchCategory.VmCreate, 1,
            async token =>
            {
                firstStarted.SetResult();
                await release.Task.WaitAsync(token);
            }, CancellationToken.None);
        var second = limiter.RunAsync(Guid.NewGuid(), NodeDispatchCategory.VmCreate, 1,
            async token =>
            {
                secondStarted.SetResult();
                await release.Task.WaitAsync(token);
            }, CancellationToken.None);

        await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(1));
        release.SetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task SeparateCategories_DoNotCreateAWholeNodeLock()
    {
        var limiter = new NodeDispatchLimiter();
        var nodeId = Guid.NewGuid();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var createStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var create = limiter.RunAsync(nodeId, NodeDispatchCategory.DockerCreate, 1,
            async token =>
            {
                createStarted.SetResult();
                await release.Task.WaitAsync(token);
            }, CancellationToken.None);
        var probe = limiter.RunAsync(nodeId, NodeDispatchCategory.Probe, 1,
            async token =>
            {
                probeStarted.SetResult();
                await release.Task.WaitAsync(token);
            }, CancellationToken.None);

        await Task.WhenAll(createStarted.Task, probeStarted.Task).WaitAsync(TimeSpan.FromSeconds(1));
        release.SetResult();
        await Task.WhenAll(create, probe);
    }

    [Fact]
    public async Task WaitForIdle_DoesNotCompleteWhileReleasedWaiterCanStillEnter()
    {
        var limiter = new NodeDispatchLimiter();
        var nodeId = Guid.NewGuid();
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = limiter.RunAsync(nodeId, NodeDispatchCategory.TeamLabExecution, 1,
            async token =>
            {
                firstStarted.SetResult();
                await releaseFirst.Task.WaitAsync(token);
            }, CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var second = limiter.RunAsync(nodeId, NodeDispatchCategory.TeamLabExecution, 1,
            async token =>
            {
                secondStarted.SetResult();
                await releaseSecond.Task.WaitAsync(token);
            }, CancellationToken.None);

        await Task.Delay(20);
        releaseFirst.SetResult();
        var idle = limiter.WaitForIdleAsync(nodeId, NodeDispatchCategory.TeamLabExecution, CancellationToken.None);

        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(idle.IsCompleted);

        releaseSecond.SetResult();
        await Task.WhenAll(first, second, idle).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LimitPolicy_UsesManifestValuesAndPlatformSafetyCaps()
    {
        var limits = new AgentExecutionLimits(100, 100, 100, 100, 100, 100, ArtifactCleanupOperations: 2);

        Assert.Equal(16, NodeDispatchLimitPolicy.Resolve(limits, NodeDispatchCategory.DockerCreate));
        Assert.Equal(4, NodeDispatchLimitPolicy.Resolve(limits, NodeDispatchCategory.VmCreate));
        Assert.Equal(4, NodeDispatchLimitPolicy.Resolve(limits, NodeDispatchCategory.TeamLabNetwork));
        Assert.Equal(16, NodeDispatchLimitPolicy.Resolve(limits, NodeDispatchCategory.Probe));
        Assert.Equal(4, NodeDispatchLimitPolicy.Resolve(limits, NodeDispatchCategory.Cleanup));
        Assert.Equal(2, NodeDispatchLimitPolicy.Resolve(limits, NodeDispatchCategory.ArtifactCleanup));
        Assert.Equal(1, NodeDispatchLimitPolicy.Resolve(null, NodeDispatchCategory.Control));
    }

    static void UpdateMaximum(ref int maximum, int current)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (current <= observed || Interlocked.CompareExchange(ref maximum, current, observed) == observed)
                return;
        }
    }
}
