using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Infrastructure;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class RuntimeFactReconciliationTests
{
    [Fact]
    public async Task MatchingVmInventory_CompletesStaleCreateAndConfirmsCapacity()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Kvm, AgentFeatureIds.Kvm);
        var vm = new VmInstance
        {
            Id = Guid.NewGuid(), ChallengeId = 10, UserId = Guid.NewGuid(), VmName = "gzctf-vm-match",
            NodeId = node.Id, Status = VmInstanceStatus.Running
        };
        var ticket = StaleVmTicket(node.Id, vm);
        context.AddRange(vm, ticket, new FleetCapacityReservation
        {
            DeploymentQueueTicketId = ticket.Id,
            WorkerNodeId = node.Id,
            VmSlots = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(vms:
            [
                new AgentRuntimeInventoryResource(Guid.NewGuid().ToString(), vm.VmName, 1, "running")
            ])
        });

        var summary = await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Succeeded, ticket.Status);
        Assert.Equal(DeploymentStage.Ready, ticket.Stage);
        Assert.Equal(CapacityReservationStatus.Confirmed,
            (await context.FleetCapacityReservations.SingleAsync()).Status);
        Assert.Equal(1, summary.RecoveredTicketCount);
        Assert.Contains(await context.OperationalEvents.Select(item => item.EventCode).ToArrayAsync(),
            code => code == OperationalEventCodes.Recovery.FactConfirmed);
    }

    [Fact]
    public async Task MissingDockerInventory_OnLocalAgent_CorrectsActiveContainerWithoutDeletingAnythingOnAgent()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        node.IsLocal = true;
        var container = new Container
        {
            Id = Guid.NewGuid(), ContainerId = "managed-container-id", Image = "registry/challenge:latest",
            NodeId = node.Id, Status = ContainerStatus.Running
        };
        context.Containers.Add(container);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(containers: [])
        });

        var summary = await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(ContainerStatus.Destroyed, container.Status);
        Assert.Equal(1, summary.MissingCount);
        Assert.Equal(1, summary.CorrectedCount);
        Assert.Contains(node.Id, agent.RequestedNodes);
        Assert.False(agent.DestroyCalled);
        var codes = await context.OperationalEvents.Select(item => item.EventCode).ToArrayAsync();
        Assert.Contains(OperationalEventCodes.Recovery.ResourceMissing, codes);
        Assert.Contains(OperationalEventCodes.Recovery.StateCorrected, codes);
        var missing = await context.OperationalEvents.SingleAsync(item =>
            item.EventCode == OperationalEventCodes.Recovery.ResourceMissing);
        Assert.Equal(OperationalEventOutcome.Failed, missing.Outcome);
        Assert.Equal(OperationalErrorCodes.RuntimeResourceMissing, missing.ErrorCode);
    }

    [Fact]
    public async Task GenerationConflict_FailsClosedAndMarksVmError()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Kvm, AgentFeatureIds.Kvm);
        var vm = new VmInstance
        {
            Id = Guid.NewGuid(), ChallengeId = 11, UserId = Guid.NewGuid(), VmName = "gzctf-vm-conflict",
            NodeId = node.Id, Status = VmInstanceStatus.Running
        };
        var ticket = StaleVmTicket(node.Id, vm);
        context.AddRange(vm, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(vms:
            [
                new AgentRuntimeInventoryResource(Guid.NewGuid().ToString(), vm.VmName, 2, "running")
            ])
        });

        var summary = await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Failed, ticket.Status);
        Assert.Equal(OperationalErrorCodes.RuntimeIdentityConflict, ticket.ErrorCode);
        Assert.Equal(VmInstanceStatus.Error, vm.Status);
        Assert.True(summary.ConflictCount >= 1);
        Assert.Contains(await context.OperationalEvents.Select(item => item.EventCode).ToArrayAsync(),
            code => code == OperationalEventCodes.Recovery.IdentityConflict);
    }

    [Fact]
    public async Task OfflineNode_DefersRecoveryAndPreservesRuntimeState()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Kvm, AgentFeatureIds.Kvm);
        node.Status = NodeStatus.Offline;
        var vm = new VmInstance
        {
            Id = Guid.NewGuid(), ChallengeId = 12, UserId = Guid.NewGuid(), VmName = "gzctf-vm-offline",
            NodeId = node.Id, Status = VmInstanceStatus.Running
        };
        var ticket = StaleVmTicket(node.Id, vm);
        context.AddRange(vm, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>());

        var summary = await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(VmInstanceStatus.Running, vm.Status);
        Assert.Equal(DeploymentQueueTicketStatus.Running, ticket.Status);
        Assert.Equal(OperationalErrorCodes.NodeOffline, ticket.ErrorCode);
        Assert.True(summary.DeferredCount >= 1);
        Assert.Empty(agent.RequestedNodes);
    }

    [Fact]
    public async Task UnsupportedInventory_DefersWithoutTreatingResourceAsMissing()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Kvm, AgentFeatureIds.Kvm, advertiseInventory: false);
        var vm = new VmInstance
        {
            Id = Guid.NewGuid(), ChallengeId = 13, UserId = Guid.NewGuid(), VmName = "gzctf-vm-legacy-agent",
            NodeId = node.Id, Status = VmInstanceStatus.Running
        };
        context.VmInstances.Add(vm);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>());

        var summary = await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(VmInstanceStatus.Running, vm.Status);
        Assert.True(summary.DeferredCount >= 1);
        Assert.Empty(agent.RequestedNodes);
        Assert.Contains(await context.OperationalEvents.Select(item => item.EventCode).ToArrayAsync(),
            code => code == OperationalEventCodes.Recovery.InventoryUnsupported);
        Assert.Equal(OperationalEventOutcome.Blocked,
            (await context.OperationalEvents.SingleAsync(item =>
                item.EventCode == OperationalEventCodes.Recovery.InventoryUnsupported)).Outcome);
    }

    [Fact]
    public async Task StoppedVm_IsNotTreatedAsAnExpectedRunningFact()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Kvm, AgentFeatureIds.Kvm);
        var vm = new VmInstance
        {
            Id = Guid.NewGuid(), ChallengeId = 14, UserId = Guid.NewGuid(), VmName = "gzctf-vm-stopped",
            NodeId = node.Id, Status = VmInstanceStatus.Stopped
        };
        context.VmInstances.Add(vm);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(vms: [])
        });

        var summary = await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(VmInstanceStatus.Stopped, vm.Status);
        Assert.Equal(0, summary.MissingCount);
    }

    [Fact]
    public async Task MissingContainerControlTarget_CompletesAndMarksResourceDestroyed()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        var container = new Container
        {
            Id = Guid.NewGuid(), ContainerId = "container-control", Image = "registry/test:latest",
            NodeId = node.Id, Status = ContainerStatus.Running, RuntimeGeneration = 3
        };
        var ticket = StaleContainerControlTicket(node.Id, container, 3);
        context.AddRange(container, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(containers: [])
        });

        await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(ContainerStatus.Destroyed, container.Status);
        Assert.Equal(DeploymentQueueTicketStatus.Succeeded, ticket.Status);
        Assert.Equal(DeploymentStage.Ready, ticket.Stage);
    }

    [Fact]
    public async Task ContainerControlGenerationMismatch_FailsClosed()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        var container = new Container
        {
            Id = Guid.NewGuid(), ContainerId = "container-generation", Image = "registry/test:latest",
            NodeId = node.Id, Status = ContainerStatus.Running, RuntimeGeneration = 2
        };
        var ticket = StaleContainerControlTicket(node.Id, container, 1);
        context.AddRange(container, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(containers:
            [
                new AgentRuntimeInventoryResource(container.ContainerId, "gzctf_container", 2, "running")
            ])
        });

        await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Failed, ticket.Status);
        Assert.Equal(OperationalErrorCodes.RuntimeIdentityConflict, ticket.ErrorCode);
        Assert.Equal(ContainerStatus.Running, container.Status);
    }

    [Fact]
    public async Task TeamLabDestroy_ReplaysOnlyWhenCurrentGenerationAssetStillExists()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        var runtime = new TeamLabRuntime
        {
            Id = 90,
            TopologyReleaseId = Guid.NewGuid(),
            Generation = 2,
            Status = TeamLabRuntimeStatus.Running,
            Shards =
            [
                new TeamLabRuntimeShard
                {
                    Generation = 2, WorkerNodeId = node.Id, Status = TeamLabRuntimeStatus.Running
                }
            ],
            Assets =
            [
                new TeamLabRuntimeAsset
                {
                    Generation = 2,
                    WorkerNodeId = node.Id,
                    Kind = TeamLabResourceKind.Docker,
                    TopologyKey = "entry",
                    Name = "Entry",
                    RuntimeResourceId = "teamlab-entry",
                    Status = TeamLabRuntimeStatus.Running
                }
            ]
        };
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(runtime.Id, 0, 0) with
        {
            Operation = RuntimeOperationKind.Destroy,
            Generation = runtime.Generation,
            TargetNodeId = node.Id
        });
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.Stage = DeploymentStage.Destroying;
        ticket.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        context.AddRange(runtime, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(containers:
            [
                new AgentRuntimeInventoryResource("teamlab-entry", "tl90-entry", 2, "running")
            ])
        });

        await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Scheduled, ticket.Status);
        Assert.Equal(TeamLabRuntimeStatus.Running, runtime.Status);
    }

    [Fact]
    public async Task TeamLabExpectedWorkload_RequiresCurrentGenerationAndActiveRuntime()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        var runtime = new TeamLabRuntime
        {
            Id = 91,
            TopologyReleaseId = Guid.NewGuid(),
            Generation = 2,
            Status = TeamLabRuntimeStatus.Failed,
            Assets =
            [
                new TeamLabRuntimeAsset
                {
                    Generation = 1,
                    WorkerNodeId = node.Id,
                    Kind = TeamLabResourceKind.Docker,
                    TopologyKey = "old-entry",
                    Name = "Old entry",
                    RuntimeResourceId = "teamlab-old-entry",
                    Status = TeamLabRuntimeStatus.Running
                },
                new TeamLabRuntimeAsset
                {
                    Generation = 2,
                    WorkerNodeId = node.Id,
                    Kind = TeamLabResourceKind.Docker,
                    TopologyKey = "failed-entry",
                    Name = "Failed entry",
                    RuntimeResourceId = "teamlab-failed-entry",
                    Status = TeamLabRuntimeStatus.Running
                }
            ]
        };
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(containers: [])
        });

        var summary = await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(0, summary.MissingCount);
        Assert.All(runtime.Assets, asset => Assert.Equal(TeamLabRuntimeStatus.Running, asset.Status));
        Assert.Equal(TeamLabRuntimeStatus.Failed, runtime.Status);
    }

    [Fact]
    public async Task ActiveTeamLabLifecycleTicket_OwnsTransientMissingAndOrphanFacts()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        var runtime = new TeamLabRuntime
        {
            Id = 92,
            TopologyReleaseId = Guid.NewGuid(),
            Generation = 3,
            Status = TeamLabRuntimeStatus.Running,
            Assets =
            [
                new TeamLabRuntimeAsset
                {
                    Generation = 3,
                    WorkerNodeId = node.Id,
                    Kind = TeamLabResourceKind.Docker,
                    TopologyKey = "entry",
                    Name = "Entry",
                    RuntimeResourceId = "teamlab-owned-entry",
                    Status = TeamLabRuntimeStatus.Running
                }
            ]
        };
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(runtime.Id, 0, 0) with
        {
            Operation = RuntimeOperationKind.Reset,
            Generation = 4
        });
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.StartedAt = DateTimeOffset.UtcNow;
        context.AddRange(runtime, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(
                containers:
                [
                    new AgentRuntimeInventoryResource(
                        "transient-native", "transient", 3, "running", RuntimeId: runtime.Id)
                ],
                teamLabResources:
                [
                    new AgentRuntimeInventoryResource(
                        "transient-control", "tl-control", 3, "running",
                        ResourceKind: "managed-switch", RuntimeId: runtime.Id)
                ])
        });

        var summary = await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(0, summary.MissingCount);
        Assert.Equal(0, summary.OrphanCount);
        Assert.Equal(TeamLabRuntimeStatus.Running, runtime.Status);
        Assert.Equal(TeamLabRuntimeStatus.Running, runtime.Assets.Single().Status);
    }

    [Fact]
    public async Task TeamLabDestroy_ControlResourceResidual_ReplaysCleanup()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        var (runtime, ticket) = StaleTeamLabDestroy(node.Id, 93);
        context.AddRange(runtime, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(
                containers: [],
                teamLabResources:
                [
                    new AgentRuntimeInventoryResource(
                        "bridge-native", "tl93-net", runtime.Generation, "running",
                        ResourceKind: "managed-switch", RuntimeId: runtime.Id)
                ])
        });

        await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Scheduled, ticket.Status);
        Assert.NotEqual(TeamLabRuntimeStatus.Destroyed, runtime.Status);
    }

    [Fact]
    public async Task TeamLabDestroy_DatabaseCleanupResidual_ReplaysCleanup()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        var (runtime, ticket) = StaleTeamLabDestroy(node.Id, 94);
        runtime.AccessGrants.Add(new TeamLabAccessGrant
        {
            Generation = runtime.Generation,
            PublicKey = "client-key",
            Revoked = false
        });
        context.AddRange(runtime, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(containers: [], teamLabResources: [])
        });

        await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Scheduled, ticket.Status);
        Assert.False(runtime.AccessGrants.Single().Revoked);
        Assert.NotEqual(TeamLabRuntimeStatus.Destroyed, runtime.Status);
    }

    [Fact]
    public async Task TeamLabReset_PlanningCheckpoint_ReplaysWithoutChangingGeneration()
    {
        await using var context = CreateContext();
        var runtime = new TeamLabRuntime
        {
            Id = 95,
            TopologyReleaseId = Guid.NewGuid(),
            Generation = 4,
            Status = TeamLabRuntimeStatus.Scheduled
        };
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(runtime.Id, 0, 0) with
        {
            Operation = RuntimeOperationKind.Reset,
            Generation = 4
        });
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        TeamLabResetCheckpointFacts.Record(
            runtime, ticket.Id, ticket.Generation, TeamLabResetCheckpoint.PlanningNextGeneration);
        context.AddRange(runtime, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>());

        await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Scheduled, ticket.Status);
        Assert.Equal(4, runtime.Generation);
    }

    [Fact]
    public async Task CreateWithoutPersistedResource_StillRequiresTicketRuntimeCapability()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Kvm, AgentFeatureIds.Kvm);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.GameContainer(1, 2, 3));
        ticket.TargetNodeId = node.Id;
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(vms: [])
        });

        await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Running, ticket.Status);
        Assert.Equal(OperationalErrorCodes.AgentFeatureMissing, ticket.ErrorCode);
    }

    [Fact]
    public async Task VmNativeIdentityMismatch_FailsClosed()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Kvm, AgentFeatureIds.Kvm);
        var vm = new VmInstance
        {
            Id = Guid.NewGuid(), ChallengeId = 15, UserId = Guid.NewGuid(), VmName = "gzctf-vm-native",
            RuntimeNativeId = Guid.NewGuid().ToString(), RuntimeGeneration = 1,
            NodeId = node.Id, Status = VmInstanceStatus.Running
        };
        var ticket = StaleVmTicket(node.Id, vm);
        context.AddRange(vm, ticket);
        await context.SaveChangesAsync();
        var agent = new InventoryAgentClient(new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(vms:
            [
                new AgentRuntimeInventoryResource(Guid.NewGuid().ToString(), vm.VmName, 1, "running")
            ])
        });

        await CreateService(context, agent).ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(DeploymentQueueTicketStatus.Failed, ticket.Status);
        Assert.Equal(VmInstanceStatus.Error, vm.Status);
        Assert.Equal(OperationalErrorCodes.RuntimeIdentityConflict, ticket.ErrorCode);
    }

    [Fact]
    public async Task OrphanObservation_IsIdempotentAcrossRepeatedReconcile()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, NodeCapability.Docker, AgentFeatureIds.Docker);
        await context.SaveChangesAsync();
        var resource = new AgentRuntimeInventoryResource(
            "orphan-container-id", "gzctf_orphan", 1, "running", "registry/orphan:latest");
        var inventories = new Dictionary<Guid, AgentRuntimeInventoryResponse>
        {
            [node.Id] = Inventory(containers: [resource])
        };
        var agent = new InventoryAgentClient(inventories);
        var service = CreateService(context, agent);

        var first = await service.ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);
        var second = await service.ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);
        inventories[node.Id] = Inventory(containers:
        [
            resource with { Generation = 2 }
        ]);
        var nextGeneration = await service.ReconcileAsync(
            Guid.CreateVersion7(), TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.Equal(1, first.OrphanCount);
        Assert.Equal(0, second.OrphanCount);
        Assert.Equal(1, nextGeneration.OrphanCount);
        Assert.Equal(2, await context.OperationalEvents.CountAsync(item =>
            item.EventCode == OperationalEventCodes.Recovery.OrphanObserved));
        Assert.False(agent.DestroyCalled);
    }

    private static RuntimeFactReconciliationService CreateService(
        AppDbContext context,
        AgentClient agent)
    {
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var capacity = new FleetCapacityReservationService(
            context,
            new LocalDevelopmentLeaseProvider(),
            new NodeCapacitySnapshotService(context),
            new NodeEligibilityEvaluator(Options.Create(new RuntimeSchedulingOptions())),
            writer,
            NullLogger<FleetCapacityReservationService>.Instance);
        return new RuntimeFactReconciliationService(
            context,
            agent,
            capacity,
            new GZCTF.Modules.TeamLab.Application.TeamLabRuntimeRecoveryPolicy(
                Options.Create(new GZCTF.Models.Internal.TeamLabNetworkConfig())),
            new PollingDeploymentQueueWakeup(),
            writer,
            NullLogger<RuntimeFactReconciliationService>.Instance);
    }

    private static (TeamLabRuntime Runtime, DeploymentQueueTicket Ticket) StaleTeamLabDestroy(
        Guid nodeId,
        int runtimeId)
    {
        var runtime = new TeamLabRuntime
        {
            Id = runtimeId,
            TopologyReleaseId = Guid.NewGuid(),
            Generation = 2,
            Status = TeamLabRuntimeStatus.Destroying,
            Shards =
            [
                new TeamLabRuntimeShard
                {
                    Generation = 2,
                    WorkerNodeId = nodeId,
                    Status = TeamLabRuntimeStatus.Destroyed
                }
            ],
            Assets =
            [
                new TeamLabRuntimeAsset
                {
                    Generation = 2,
                    WorkerNodeId = nodeId,
                    Kind = TeamLabResourceKind.Docker,
                    TopologyKey = "entry",
                    Name = "Entry",
                    RuntimeResourceId = $"teamlab-{runtimeId}-entry",
                    Status = TeamLabRuntimeStatus.Destroyed
                }
            ]
        };
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TeamLab(runtime.Id, 0, 0) with
        {
            Operation = RuntimeOperationKind.Destroy,
            Generation = runtime.Generation,
            TargetNodeId = nodeId
        });
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.Stage = DeploymentStage.Destroying;
        ticket.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        return (runtime, ticket);
    }

    private static WorkerNode SeedNode(
        AppDbContext context,
        NodeCapability capability,
        string runtimeFeature,
        bool advertiseInventory = true)
    {
        var features = new List<string> { runtimeFeature };
        if (advertiseInventory)
            features.Add(AgentFeatureIds.RuntimeInventory);
        var normalized = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest(
            "phase7-test",
            null,
            AgentCapabilityEvaluator.SupportedManifestSchema,
            features.ToArray(),
            new AgentExecutionLimits(
                capability.HasFlag(NodeCapability.Docker) ? 2 : 0,
                capability.HasFlag(NodeCapability.Kvm) ? 1 : 0,
                capability.HasFlag(NodeCapability.Docker) ? 1 : 0,
                capability.HasFlag(NodeCapability.Kvm) ? 1 : 0,
                0,
                1),
            new AgentHostFacts(8, 16L * 1024 * 1024 * 1024, 0,
                capability.HasFlag(NodeCapability.Kvm), capability.HasFlag(NodeCapability.Kvm)),
            DateTimeOffset.UtcNow));
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "worker",
            HostAddress = "10.24.0.31",
            AuthToken = "token",
            Status = NodeStatus.Online,
            LastHeartbeat = DateTimeOffset.UtcNow,
            IsSchedulable = true,
            Capabilities = capability,
            CapabilityManifestJson = normalized.Json,
            CapabilityManifestSchemaVersion = AgentCapabilityEvaluator.SupportedManifestSchema,
            CapabilityHash = normalized.Hash,
            MaxContainers = 10,
            MaxVms = 10
        };
        context.WorkerNodes.Add(node);
        return node;
    }

    private static DeploymentQueueTicket StaleVmTicket(Guid nodeId, VmInstance vm)
    {
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.Vm(
            1, vm.UserId, vm.ChallengeId, vm.Id) with { Generation = vm.RuntimeGeneration });
        ticket.TargetNodeId = nodeId;
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.Stage = DeploymentStage.VmCreating;
        ticket.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        ticket.ClaimOwner = "lost-worker";
        ticket.ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        return ticket;
    }

    private static DeploymentQueueTicket StaleContainerControlTicket(
        Guid nodeId,
        Container container,
        int generation)
    {
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.MaintenanceContainer(
            container.Id, nodeId, container.Image, generation));
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.Stage = DeploymentStage.Destroying;
        ticket.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        ticket.ClaimOwner = "lost-worker";
        ticket.ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        return ticket;
    }

    private static AgentRuntimeInventoryResponse Inventory(
        IReadOnlyList<AgentRuntimeInventoryResource>? containers = null,
        IReadOnlyList<AgentRuntimeInventoryResource>? vms = null,
        IReadOnlyList<AgentRuntimeInventoryResource>? teamLabResources = null) => new(
        containers is not null,
        vms is not null,
        containers ?? [],
        vms ?? [],
        DateTimeOffset.UtcNow,
        teamLabResources);

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class InventoryAgentClient(
        IReadOnlyDictionary<Guid, AgentRuntimeInventoryResponse> inventories)
        : AgentClient(
            new Mock<IHttpClientFactory>().Object,
            new Mock<IServiceScopeFactory>().Object,
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance)
    {
        public List<Guid> RequestedNodes { get; } = [];
        public bool DestroyCalled { get; private set; }

        public override Task<AgentRuntimeInventoryResponse> GetRuntimeInventoryAsync(
            Guid nodeId,
            CancellationToken token)
        {
            RequestedNodes.Add(nodeId);
            return Task.FromResult(inventories[nodeId]);
        }

        public override Task DestroyContainerAsync(Guid nodeId, string containerId, CancellationToken token)
        {
            DestroyCalled = true;
            return Task.CompletedTask;
        }
    }
}
