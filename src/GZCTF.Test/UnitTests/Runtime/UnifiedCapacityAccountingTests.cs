using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class UnifiedCapacityAccountingTests
{
    [Fact]
    public async Task Snapshot_SubtractsOrdinaryAndTeamLabReservationsTogether()
    {
        await using var context = CreateContext();
        var node = CreateNode(NodeCapability.Docker | NodeCapability.Kvm);
        var ordinary = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 3));
        var teamLab = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(4, 0, 1));
        context.AddRange(node, ordinary, teamLab);
        context.FleetCapacityReservations.AddRange(
            Reservation(ordinary.Id, node.Id, new WorkloadResourceVector(10, 2_048, 4_096, 1, 0)),
            Reservation(teamLab.Id, node.Id, new WorkloadResourceVector(20, 8_192, 40_000, 0, 1)));
        await context.SaveChangesAsync();

        var snapshot = Assert.Single(await new NodeCapacitySnapshotService(context)
            .LoadAsync(CancellationToken.None));

        Assert.Equal(new WorkloadResourceVector(30, 10_240, 44_096, 1, 1), snapshot.Reserved);
        Assert.Equal(snapshot.Total - snapshot.Actual - snapshot.Reserved - snapshot.SafetyMargin,
            snapshot.Available);
    }

    [Fact]
    public async Task DockerOnlyNode_RemainsEligibleForDockerWithoutKvm()
    {
        await using var context = CreateContext();
        context.WorkerNodes.Add(CreateNode(NodeCapability.Docker));
        await context.SaveChangesAsync();
        var snapshot = Assert.Single(await new NodeCapacitySnapshotService(context)
            .LoadAsync(CancellationToken.None));
        var evaluator = new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions()));

        var reason = evaluator.GetReason(
            snapshot,
            NodeCapability.Docker,
            new WorkloadResourceVector(1, 64, 256, 1, 0),
            requireTeamLab: false);

        Assert.Null(reason);
    }

    static FleetCapacityReservation Reservation(
        Guid ticketId,
        Guid nodeId,
        WorkloadResourceVector resources) => new()
    {
        DeploymentQueueTicketId = ticketId,
        WorkerNodeId = nodeId,
        CpuUnits = resources.CpuUnits,
        MemoryMiB = resources.MemoryMiB,
        StorageMiB = resources.StorageMiB,
        DockerSlots = resources.DockerSlots,
        VmSlots = resources.VmSlots,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
    };

    static WorkerNode CreateNode(NodeCapability capabilities)
    {
        var manifest = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest(
            "test", null, 1,
            capabilities.HasFlag(NodeCapability.Kvm)
                ? [AgentFeatureIds.Docker, AgentFeatureIds.Kvm]
                : [AgentFeatureIds.Docker],
            new AgentExecutionLimits(2, 1, 2, 1),
            new AgentHostFacts(8, 16L * 1024 * 1024 * 1024, 100L * 1024 * 1024 * 1024),
            DateTimeOffset.UtcNow));
        return new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "capacity-node",
            HostAddress = "127.0.0.1",
            AuthToken = "token",
            IsLocal = true,
            IsSchedulable = true,
            Status = NodeStatus.Online,
            Capabilities = capabilities,
            MaxContainers = 10,
            MaxVms = 4,
            CapabilityManifestJson = manifest.Json,
            CapabilityManifestSchemaVersion = AgentCapabilityEvaluator.SupportedManifestSchema,
            CapabilityHash = manifest.Hash
        };
    }

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
