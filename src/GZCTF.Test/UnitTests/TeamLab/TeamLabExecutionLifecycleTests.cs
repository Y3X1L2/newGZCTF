using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts.Execution;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabExecutionLifecycleTests
{
    [Fact]
    public void AgentAssembly_DoesNotContainUnusedLibvirtEventDispatcher()
    {
        var dispatcher = typeof(KvmService).Assembly.GetType(
            "GZCTF.Agent.Services.Vm.LibvirtEventDispatcher",
            throwOnError: false);

        Assert.Null(dispatcher);
    }

    [Fact]
    public void Executor_DoesNotUseRemovableRawSemaphoreDictionary()
    {
        var field = typeof(TeamLabExecutionPlanExecutor).GetField(
            "executionLocks",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.False(field.FieldType.IsGenericType &&
                     field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>));
    }

    [Fact]
    public async Task KeyedSemaphoreRegistry_KeepsLaterCallersSerializedWhileAWaiterOwnsTheKey()
    {
        var registry = new KeyedSemaphoreRegistry<string>();
        using var first = await registry.AcquireAsync("shard-a", CancellationToken.None);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Run(async () =>
        {
            using var lease = await registry.AcquireAsync("shard-a", CancellationToken.None);
            secondEntered.SetResult();
            await releaseSecond.Task;
        });

        first.Dispose();
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var third = registry.AcquireAsync("shard-a", CancellationToken.None).AsTask();

        Assert.False(third.IsCompleted);
        releaseSecond.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        using var thirdLease = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RuntimeInventory_UsesTheSameWireTypesOnAgentAndControlPlane()
    {
        var agentMethod = typeof(DockerService).GetMethod(nameof(DockerService.GetManagedRuntimeInventoryAsync));
        var clientMethod = typeof(AgentClient).GetMethod(nameof(AgentClient.GetRuntimeInventoryAsync));

        Assert.NotNull(agentMethod);
        Assert.NotNull(clientMethod);
        var agentListType = agentMethod.ReturnType.GetGenericArguments().Single();
        var agentResourceType = agentListType.GetGenericArguments().Single();
        var clientResponseType = clientMethod.ReturnType.GetGenericArguments().Single();

        Assert.Same(typeof(AgentRuntimeInventoryResource), agentResourceType);
        Assert.Same(typeof(AgentRuntimeInventoryResponse), clientResponseType);
    }

    [Fact]
    public void Journal_EvictsOldestEntryWhenCapacityIsReached()
    {
        var journal = new TeamLabExecutionEventJournal();
        for (var runtimeId = 1; runtimeId <= 4096; runtimeId++)
        {
            var plan = Plan(runtimeId);
            journal.Save(plan, Response(plan));
        }

        var overflow = Plan(4097);
        journal.Save(overflow, Response(overflow));
        Assert.True(journal.TryGet(overflow, out _));
        Assert.False(journal.TryGet(Plan(1), out _));
    }

    [Fact]
    public void Journal_EnforcesCapacityUnderConcurrentWriters()
    {
        var journal = new TeamLabExecutionEventJournal();

        Parallel.For(1, 8193, runtimeId =>
        {
            var plan = Plan(runtimeId);
            try
            {
                journal.Save(plan, Response(plan));
            }
            catch (InvalidOperationException)
            {
                // Capacity rejection is the expected result after the journal fills.
            }
        });

        var plans = typeof(TeamLabExecutionEventJournal).GetField(
            "plans",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(journal);
        var count = (int?)plans?.GetType().GetProperty("Count")?.GetValue(plans);

        Assert.NotNull(count);
        Assert.InRange(count.Value, 0, 4096);
    }

    private static TeamLabExecutionPlanV2 Plan(int runtimeId) => new(
        runtimeId,
        Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001"),
        1,
        "shard-a",
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        [],
        [],
        []);

    private static TeamLabExecutionPlanApplyResponse Response(TeamLabExecutionPlanV2 plan) =>
        new(true, false, plan.PlanDigest, [], []);
}
