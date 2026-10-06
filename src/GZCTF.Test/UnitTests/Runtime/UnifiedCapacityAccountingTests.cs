using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task Snapshot_UsesWorkloadStorageBudgetAndSubtractsAllActiveReservations()
    {
        await using var context = CreateContext();
        var node = CreateNode(NodeCapability.Docker | NodeCapability.Kvm, dockerStorageGiB: 70,
            vmStorageGiB: 180);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 3));
        context.AddRange(node, ticket);
        context.FleetCapacityReservations.Add(Reservation(ticket.Id, node.Id,
            new WorkloadResourceVector(0, 0, 20 * 1024, 1, 0)));
        await context.SaveChangesAsync();

        var snapshot = Assert.Single(await new NodeCapacitySnapshotService(context)
            .LoadAsync(CancellationToken.None));
        var evaluator = new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions()));
        Assert.Equal(50 * 1024, snapshot.AvailableFor(new(0, 0, 0, 1, 0)).StorageMiB);
        Assert.Equal(160 * 1024, snapshot.AvailableFor(new(0, 0, 0, 0, 1)).StorageMiB);
        Assert.Equal(50 * 1024, snapshot.AvailableFor(new(0, 0, 0, 1, 1)).StorageMiB);
        Assert.Equal("node_storage_capacity_exhausted", evaluator.GetReason(snapshot,
            NodeCapability.Docker, new(0, 0, 60 * 1024, 1, 0), false));
        Assert.Equal("node_storage_capacity_exhausted", evaluator.GetReason(snapshot,
            NodeCapability.Docker, new(0, 0, 60 * 1024, 1, 0), false,
            ignoreDynamicLoad: true));
        Assert.Null(evaluator.GetReason(snapshot, NodeCapability.Kvm,
            new(0, 0, 150 * 1024, 0, 1), false));
        Assert.Null(evaluator.GetReason(snapshot, NodeCapability.Kvm,
            new(0, 0, 80 * 1024, 0, 4), false));
        Assert.Equal("node_storage_capacity_exhausted", evaluator.GetReason(snapshot,
            NodeCapability.Docker | NodeCapability.Kvm, new(0, 0, 60 * 1024, 1, 1), false));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0L, false)]
    [InlineData(70L, true)]
    public async Task DockerStorage_NullFallsBackToLegacyButMeasuredZeroBlocks(long? dockerStorageGiB,
        bool expectedFit)
    {
        await using var context = CreateContext();
        context.WorkerNodes.Add(CreateNode(NodeCapability.Docker, dockerStorageGiB, vmStorageGiB: 180));
        await context.SaveChangesAsync();

        var snapshot = Assert.Single(await new NodeCapacitySnapshotService(context)
            .LoadAsync(CancellationToken.None));
        var request = new WorkloadResourceVector(0, 0, 60 * 1024, 1, 0);
        Assert.Equal(expectedFit, snapshot.Fits(request));
        Assert.Equal(expectedFit ? null : "node_storage_capacity_exhausted",
            new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions()))
                .GetReason(snapshot, NodeCapability.Docker, request, false));
    }

    [Fact]
    public void MixedWorkload_OnOneFilesystemDoesNotSumFreeSpace()
    {
        var snapshot = new NodeCapacitySnapshot(CreateNode(NodeCapability.Docker | NodeCapability.Kvm),
            0, 0, 0, 0, 0, 0,
            ResourceTotal: new WorkloadResourceVector(80, 16_384, 70 * 1024, 0, 0),
            DockerStorageTotalMiB: 70 * 1024);
        var request = new WorkloadResourceVector(0, 0, 100 * 1024, 1, 1);

        Assert.Equal(70 * 1024, snapshot.AvailableFor(request).StorageMiB);
        Assert.False(snapshot.Fits(request));
    }

    [Fact]
    public async Task Reservation_SelectsNodesUsingRequestedWorkloadStorageBudget()
    {
        await using var context = CreateContext();
        var vmNode = CreateNode(NodeCapability.Docker | NodeCapability.Kvm, 70, 180);
        var dockerNode = CreateNode(NodeCapability.Docker | NodeCapability.Kvm, 180, 70);
        context.WorkerNodes.AddRange(vmNode, dockerNode);
        await context.SaveChangesAsync();
        var reservations = new FleetCapacityReservationService(context,
            new LocalDevelopmentLeaseProvider(), NullLogger<FleetCapacityReservationService>.Instance);

        var vm = await reservations.TryReserveAsync(Guid.NewGuid(), new FleetCapacityRequest(
            NodeCapability.Kvm, new WorkloadResourceVector(0, 0, 80 * 1024, 0, 4)),
            CancellationToken.None);
        var docker = await reservations.TryReserveAsync(Guid.NewGuid(), new FleetCapacityRequest(
            NodeCapability.Docker, new WorkloadResourceVector(0, 0, 80 * 1024, 1, 0)),
            CancellationToken.None);

        Assert.True(vm.Success, vm.Message);
        Assert.Equal(vmNode.Id, vm.NodeId);
        Assert.True(docker.Success, docker.Message);
        Assert.Equal(dockerNode.Id, docker.NodeId);
        Assert.Equal(2, await context.FleetCapacityReservations.CountAsync());
    }

    [Fact]
    public async Task AutomaticCapacity_UsesReportedResourcesInsteadOfManualSlotLimits()
    {
        await using var context = CreateContext();
        var node = CreateNode(NodeCapability.Docker | NodeCapability.Kvm);
        node.AutomaticCapacity = true;
        node.MaxContainers = 0;
        node.MaxVms = 0;
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var snapshot = Assert.Single(await new NodeCapacitySnapshotService(context)
            .LoadAsync(CancellationToken.None));
        var evaluator = new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions()));

        Assert.Null(evaluator.GetReason(snapshot, NodeCapability.Docker,
            new WorkloadResourceVector(10, 512, 256, 1, 0), requireTeamLab: false));
        Assert.Null(evaluator.GetReason(snapshot, NodeCapability.Kvm,
            new WorkloadResourceVector(10, 512, 256, 0, 1), requireTeamLab: false));
    }

    [Fact]
    public async Task ManualCapacity_StillAppliesOperatorSlotLimits()
    {
        await using var context = CreateContext();
        var node = CreateNode(NodeCapability.Docker | NodeCapability.Kvm);
        node.AutomaticCapacity = false;
        node.MaxContainers = 0;
        node.MaxVms = 0;
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var snapshot = Assert.Single(await new NodeCapacitySnapshotService(context)
            .LoadAsync(CancellationToken.None));
        var evaluator = new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions()));

        Assert.Equal("node_docker_slots_exhausted", evaluator.GetReason(snapshot, NodeCapability.Docker,
            new WorkloadResourceVector(10, 512, 256, 1, 0), requireTeamLab: false));
        Assert.Equal("node_vm_slots_exhausted", evaluator.GetReason(snapshot, NodeCapability.Kvm,
            new WorkloadResourceVector(10, 512, 256, 0, 1), requireTeamLab: false));
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

    static WorkerNode CreateNode(NodeCapability capabilities, long? dockerStorageGiB = null,
        long vmStorageGiB = 100)
    {
        var manifest = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest(
            "test", null, 1,
            capabilities.HasFlag(NodeCapability.Kvm)
                ? [AgentFeatureIds.Docker, AgentFeatureIds.Kvm]
                : [AgentFeatureIds.Docker],
            new AgentExecutionLimits(2, 1, 2, 1),
            new AgentHostFacts(8, 16L * 1024 * 1024 * 1024,
                vmStorageGiB * 1024 * 1024 * 1024,
                AvailableDockerStorageBytes: dockerStorageGiB * 1024 * 1024 * 1024),
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
