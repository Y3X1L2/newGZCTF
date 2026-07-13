using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.Extensions.Options;
using Xunit;
using AgentHostFacts = GZCTF.Agent.Models.AgentHostFacts;
using AgentLimits = GZCTF.Agent.Models.AgentExecutionLimits;
using AgentManifest = GZCTF.Agent.Models.AgentCapabilityManifest;
using PlatformManifest = GZCTF.Modules.Runtime.Contracts.AgentCapabilityManifest;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentCapabilityContractTests
{
    [Fact]
    public void CapabilityManifest_RoundTripsAcrossAgentAndPlatformContracts()
    {
        var source = new AgentManifest("1.8.3", "sha256", 1,
            [AgentFeatureIds.Docker, AgentFeatureIds.TeamLabFabric],
            new AgentLimits(4, 2, 2, 1, 4, 2),
            new AgentHostFacts(16, 32L * 1024 * 1024 * 1024, true, true),
            DateTimeOffset.Parse("2026-07-13T00:00:00Z"));

        var json = JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var target = JsonSerializer.Deserialize<PlatformManifest>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(target);
        Assert.Equal(source.AgentVersion, target.AgentVersion);
        Assert.Equal(source.ManifestSchemaVersion, target.ManifestSchemaVersion);
        Assert.Equal(source.Features, target.Features);
        Assert.Equal(source.ExecutionLimits.DockerCreates, target.ExecutionLimits.DockerCreates);
        Assert.Equal(source.Host.LogicalCpu, target.Host.LogicalCpu);
    }

    [Fact]
    public async Task ImageTransferSingleFlight_ExecutesOneTransferForConcurrentWaiters()
    {
        var singleFlight = new ImageTransferSingleFlight();
        var executions = 0;
        var waiters = Enumerable.Range(0, 20).Select(_ => singleFlight.RunAsync("image:key", async token =>
        {
            Interlocked.Increment(ref executions);
            await Task.Delay(50, token);
            return 42;
        }, CancellationToken.None));

        var results = await Task.WhenAll(waiters);

        Assert.Equal(1, executions);
        Assert.All(results, value => Assert.Equal(42, value));
    }

    [Fact]
    public async Task OperationGate_EnforcesCategoryLimitWithoutSerializingOtherCategories()
    {
        var gate = new AgentOperationGate(Options.Create(new AgentConfig
        {
            ExecutionLimits = new AgentExecutionLimitOverrides
            {
                DockerCreates = 2,
                VmCreates = 1
            }
        }));
        var active = 0;
        var maximum = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var permit = await gate.EnterAsync(AgentOperationCategory.DockerCreate,
                CancellationToken.None);
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximum, current);
            if (current == 2) started.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref active);
        }).ToArray();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await using var vmPermit = await gate.EnterAsync(AgentOperationCategory.VmCreate, CancellationToken.None);
        Assert.Equal(2, maximum);
        release.SetResult();
        await Task.WhenAll(tasks);
    }

    static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
