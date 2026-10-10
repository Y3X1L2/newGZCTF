using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.Storage.Interface;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class TeamLabRecordDeletionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task DestroyThenDeleteRecord_RemovesOwnedRowsAndPreservesImagesAndOtherRuntime()
    {
        await using var context = await ContextAsync();
        var (runtime, owner, node) = await SeedAsync(context);
        var other = new TeamLabRuntime { TopologyReleaseId = runtime.TopologyReleaseId, Status = TeamLabRuntimeStatus.Destroyed };
        context.Add(other);
        // Normal successful creation confirms the capacity reservation and sets
        // ReleasedAt: the terminal history is not an active runtime reference.
        var creationTicket = new DeploymentQueueTicket { Kind = DeploymentQueueKind.TeamLabRuntime,
            TeamLabRuntimeId = runtime.Id, Status = DeploymentQueueTicketStatus.Succeeded };
        context.Add(new FleetCapacityReservation { DeploymentQueueTicket = creationTicket, WorkerNodeId = node,
            Status = CapacityReservationStatus.Confirmed, ReleasedAt = DateTimeOffset.UtcNow });
        var networkLease = new TeamLabNetworkLease { RuntimeId = runtime.Id, TopologyReleaseId = runtime.TopologyReleaseId,
            NetworkKey = "inside", AllocatedCidr = System.Net.IPNetwork.Parse("10.45.1.0/24") };
        context.Add(networkLease);
        await context.SaveChangesAsync();
        runtime.Networks.Add(new TeamLabRuntimeNetwork { RuntimeId = runtime.Id, ShardId = runtime.Shards[0].Id,
            WorkerNodeId = node, NetworkLeaseId = networkLease.Id, TopologyKey = "inside", Cidr = "10.45.1.0/24" });
        var session = new TeamLabRemoteSession
        {
            RuntimeId = runtime.Id, RuntimeAssetId = runtime.Assets[0].Id, WorkerNodeId = node,
            RequestedByUserId = owner, Status = TeamLabRemoteSessionStatus.Ended, EndedAt = DateTimeOffset.UtcNow
        };
        context.Add(session);
        context.Add(new TeamLabTrafficFlow { RuntimeId = runtime.Id });
        await context.SaveChangesAsync();
        var file = new TeamLabRemoteAuditFile { SessionId = session.Id, RelativePath = $"teamlab/remote-audit/{session.PublicId:N}.json" };
        context.Add(file);
        await context.SaveChangesAsync();
        var executor = Executor();
        executor.Setup(item => item.RemoveAccessAsync(It.IsAny<Guid>(), It.IsAny<TeamLabNodeAccessRemoveRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeResult.Ok());
        var remote = new Mock<ITeamLabRemoteAccessService>();
        var captures = new Mock<ITeamLabCaptureCleanup>();
        captures.Setup(item => item.ExpireGenerationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        var accesses = new Mock<ITeamLabServiceAccessCleanup>();
        accesses.Setup(item => item.CleanupRuntimeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        var cleanupEvents = new TeamLabEventRecorder(context, Mock.Of<IOperationalEventWriter>(), new OperationalCorrelation());
        var traffic = new TeamLabTrafficApplicationService(context, executor.Object, new LocalDevelopmentLeaseProvider(),
            null!, cleanupEvents, NullLogger<TeamLabTrafficApplicationService>.Instance);
        var cleanup = new TeamLabRuntimeCleanupService(context, executor.Object, traffic, captures.Object, null!,
            new TeamLabEventRecorder(context, Mock.Of<IOperationalEventWriter>(), new OperationalCorrelation()),
            remote.Object, accesses.Object, Preparation(context));
        // Use the normal cleanup/finalization implementation. Recovery snapshots are
        // absent afterwards; that is expected, and must not make deletion impossible.
        runtime.Status = TeamLabRuntimeStatus.Running;
        await context.SaveChangesAsync();
        var cleaned = await cleanup.CleanupAsync(runtime, markDestroyedOnSuccess: true, default);
        Assert.True(cleaned.Success);
        Assert.Empty(await context.TeamLabExecutionPlanSnapshots.ToArrayAsync());
        context.ChangeTracker.Clear();
        var storage = new Mock<IBlobStorage>();
        await Service(context, executor, storage).DeleteRuntimeAsync(runtime.PublicId, owner, true, default);
        context.ChangeTracker.Clear();
        Assert.False(await context.TeamLabRuntimes.AnyAsync(item => item.Id == runtime.Id));
        Assert.True(await context.TeamLabRuntimes.AnyAsync(item => item.Id == other.Id));
        Assert.Empty(await context.TeamLabRuntimeAssets.ToArrayAsync());
        Assert.Empty(await context.TeamLabRuntimeShards.ToArrayAsync());
        Assert.Empty(await context.TeamLabRuntimeNetworks.ToArrayAsync());
        Assert.Empty(await context.TeamLabNetworkLeases.ToArrayAsync());
        Assert.Empty(await context.TeamLabTrafficFlows.ToArrayAsync());
        Assert.Empty(await context.TeamLabRemoteSessions.ToArrayAsync());
        Assert.Empty(await context.Set<TeamLabRemoteAuditFile>().ToArrayAsync());
        Assert.Single(await context.ImageTemplates.ToArrayAsync());
        storage.Verify(item => item.DeleteAsync(file.RelativePath, It.IsAny<CancellationToken>()), Times.Once);
        await Service(context, executor, storage).DeleteRuntimeAsync(runtime.PublicId, owner, true, default);
    }

    [Theory]
    [InlineData("running", "runtime_not_destroyed")]
    [InlineData("queue", "runtime_cleanup_pending")]
    [InlineData("cache", "runtime_cleanup_pending")]
    [InlineData("lease", "runtime_cleanup_pending")]
    [InlineData("business", "runtime_in_use")]
    [InlineData("vm", "runtime_cleanup_pending")]
    [InlineData("offline", "runtime_inventory_unavailable")]
    [InlineData("empty-unavailable", "runtime_inventory_unavailable")]
    [InlineData("owner", "insufficient_permission")]
    public async Task UnsafeOrUnauthorizedDeletion_PreservesRuntime(string condition, string expected)
    {
        await using var context = await ContextAsync();
        var (runtime, owner, node) = await SeedAsync(context);
        var executor = Executor();
        var usage = new Mock<ITeamLabUsageProjectionProvider>();
        usage.Setup(item => item.GetGameBoundRuntimeIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<int>());
        switch (condition)
        {
            case "running": runtime.Status = TeamLabRuntimeStatus.Running; break;
            case "queue": context.Add(new DeploymentQueueTicket { Kind = DeploymentQueueKind.TeamLabRuntime, TeamLabRuntimeId = runtime.Id }); break;
            case "cache":
                var record = new ImageDistributionRecord { ImageTemplateId = 7, WorkerNodeId = node };
                context.Add(new ImageDistributionReference { DistributionRecord = record, Kind = ImageDistributionReferenceKind.TeamLabRuntime, ResourceId = runtime.Id });
                break;
            case "lease": context.Add(new TeamLabNetworkLease { RuntimeId = runtime.Id, TopologyReleaseId = runtime.TopologyReleaseId, AllocatedCidr = System.Net.IPNetwork.Parse("10.45.1.0/24") }); break;
            case "business": usage.Setup(item => item.GetGameBoundRuntimeIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<int> { runtime.Id }); break;
            case "vm": executor.Setup(item => item.GetRuntimeInventoryAsync(node, It.IsAny<CancellationToken>())).ReturnsAsync(new TeamLabNodeRuntimeInventory([], [new("native", runtime.Assets[0].RuntimeResourceId!, 1, "running", null)], [], DateTimeOffset.UtcNow, true, true)); break;
            case "offline": executor.Setup(item => item.GetRuntimeInventoryAsync(node, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException()); break;
            case "empty-unavailable": executor.Setup(item => item.GetRuntimeInventoryAsync(node, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TeamLabNodeRuntimeInventory([], [], [], DateTimeOffset.UtcNow, true, false)); break;
            case "owner": owner = Guid.CreateVersion7(); break;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            Service(context, executor, usage: usage.Object).DeleteRuntimeAsync(runtime.PublicId, owner, condition != "owner", default));
        Assert.Equal(expected, error.Code);
        Assert.True(await context.TeamLabRuntimes.AnyAsync(item => item.Id == runtime.Id));
    }

    [Fact]
    public async Task AuditDeleteFailure_KeepsMetadataForRetry_AndNeverDeletesUnrelatedPath()
    {
        await using var context = await ContextAsync();
        var (runtime, owner, node) = await SeedAsync(context);
        var session = new TeamLabRemoteSession { RuntimeId = runtime.Id, RuntimeAssetId = runtime.Assets[0].Id, WorkerNodeId = node,
            RequestedByUserId = owner, Status = TeamLabRemoteSessionStatus.Ended };
        context.Add(session);
        await context.SaveChangesAsync();
        var path = $"teamlab/remote-audit/{session.PublicId:N}.json";
        context.Add(new TeamLabRemoteAuditFile { SessionId = session.Id, RelativePath = path });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var storage = new Mock<IBlobStorage>();
        storage.Setup(item => item.DeleteAsync(path, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException());
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            Service(context, storage: storage).DeleteRuntimeAsync(runtime.PublicId, owner, true, default));
        Assert.Equal("runtime_artifact_cleanup_pending", error.Code);
        context.ChangeTracker.Clear();
        Assert.Single(await context.Set<TeamLabRemoteAuditFile>().ToArrayAsync());
        storage.Setup(item => item.DeleteAsync(path, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storage.Setup(item => item.ExistsAsync(path, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            Service(context, storage: storage).DeleteRuntimeAsync(runtime.PublicId, owner, true, default));
        Assert.Equal("runtime_artifact_cleanup_pending", error.Code);
        context.ChangeTracker.Clear();
        Assert.Single(await context.Set<TeamLabRemoteAuditFile>().ToArrayAsync());
        storage.Setup(item => item.ExistsAsync(path, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Service(context, storage: storage).DeleteRuntimeAsync(runtime.PublicId, owner, true, default);
        Assert.Empty(await context.TeamLabRuntimes.ToArrayAsync());
        storage.Verify(item => item.DeleteAsync(It.Is<string>(value => value != path), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SceneDeletion_ProtectsRuntimeAndBusinessBinding_ThenDeletesPublishedVersion()
    {
        await using var context = await ContextAsync();
        var (runtime, owner, _) = await SeedAsync(context);
        var topology = await context.TeamLabTopologies.SingleAsync();
        var service = Service(context);
        var blocked = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.DeleteTopologyAsync(topology.PublicId, owner, true, default));
        Assert.Equal("topology_in_use", blocked.Code);
        await service.DeleteRuntimeAsync(runtime.PublicId, owner, true, default);
        context.ChangeTracker.Clear();
        var usage = new Mock<ITeamLabUsageProjectionProvider>();
        usage.Setup(item => item.GetGameBindingCountsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int> { [topology.Id] = 1 });
        blocked = await Assert.ThrowsAsync<TeamLabApiContractException>(() => Service(context, usage: usage.Object).DeleteTopologyAsync(topology.PublicId, owner, true, default));
        Assert.Equal("topology_in_use", blocked.Code);
        context.ChangeTracker.Clear();
        await service.DeleteTopologyAsync(topology.PublicId, owner, true, default);
        Assert.Empty(await context.TeamLabTopologies.ToArrayAsync());
        Assert.Empty(await context.TeamLabTopologyReleases.ToArrayAsync());
        Assert.Single(await context.ImageTemplates.ToArrayAsync());
        await service.DeleteTopologyAsync(topology.PublicId, owner, true, default);
    }

    private async Task<AppDbContext> ContextAsync()
    {
        var database = "delete_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
        await command.ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = database };
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task ConcurrentRecordAndSceneDeletes_AreIdempotentAndLeaveNoOwnedRows()
    {
        await using var seed = await ContextAsync();
        var (runtime, owner, _) = await SeedAsync(seed);
        var topologyId = await seed.TeamLabTopologies.Select(item => item.PublicId).SingleAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(seed.Database.GetConnectionString()).Options;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = Executor();
        executor.Setup(item => item.GetRuntimeInventoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.TrySetResult();
                await proceed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return new TeamLabNodeRuntimeInventory([], [], [], DateTimeOffset.UtcNow, true, true);
            });
        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        var firstDelete = Service(first, executor).DeleteRuntimeAsync(runtime.PublicId, owner, true, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondDelete = Service(second).DeleteRuntimeAsync(runtime.PublicId, owner, true, default);
        proceed.TrySetResult();
        await Task.WhenAll(firstDelete, secondDelete).WaitAsync(TimeSpan.FromSeconds(20));
        await using var third = new AppDbContext(options);
        await using var fourth = new AppDbContext(options);
        await Task.WhenAll(Service(third).DeleteTopologyAsync(topologyId, owner, true, default),
            Service(fourth).DeleteTopologyAsync(topologyId, owner, true, default));
        Assert.Empty(await seed.TeamLabRuntimes.AsNoTracking().ToArrayAsync());
        Assert.Empty(await seed.TeamLabTopologies.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task DeletedCreationKey_CannotReplayIntoANewRuntime()
    {
        await using var context = await ContextAsync();
        var (runtime, owner, _) = await SeedAsync(context);
        runtime.CreationIdempotencyKey = "retired-create-key";
        runtime.CreateRequestHash = "original-create-hash";
        await context.SaveChangesAsync();
        await Service(context).DeleteRuntimeAsync(runtime.PublicId, owner, true, default);
        context.ChangeTracker.Clear();
        var history = new TeamLabRecordReferenceQuery(context);
        var planner = new TeamLabRuntimePlanner(context, null!, null!, null!, Options.Create(new TeamLabNetworkConfig()), history);
        var request = new GZCTF.Modules.TeamLab.Contracts.CreateTeamLabRuntimeModel(runtime.TopologyReleaseId, null, null, null);
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            planner.CreateAsync(request, owner, "original-create-hash", "retired-create-key", null, default));
        Assert.Equal("runtime_record_deleted", error.Code);
        Assert.Equal(410, error.StatusCode);
        Assert.Empty(await context.TeamLabRuntimes.ToArrayAsync());
        var receipt = Assert.Single(await context.ApiOperations.ToArrayAsync());
        Assert.Equal("teamlab-runtime-deleted", receipt.ResourceType);
        Assert.Equal(owner, receipt.ActorUserId);
        Assert.Equal(GZCTF.Modules.Audit.Domain.ApiOperationStatus.Succeeded, receipt.Status);
    }

    [Fact]
    public async Task DockerOnlyRecordDeletion_DoesNotRequireKvmInventory()
    {
        await using var context = await ContextAsync();
        var (runtime, owner, node) = await SeedAsync(context);
        runtime.Assets[0].Kind = TeamLabResourceKind.Docker;
        await context.SaveChangesAsync();
        var executor = Executor();
        executor.Setup(item => item.GetRuntimeInventoryAsync(node, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabNodeRuntimeInventory([], [], [], DateTimeOffset.UtcNow, true, false));
        await Service(context, executor).DeleteRuntimeAsync(runtime.PublicId, owner, true, default);
        Assert.Empty(await context.TeamLabRuntimes.ToArrayAsync());
    }

    [Fact]
    public async Task BusyAuditLease_ReturnsConflictAndPreservesRecord()
    {
        await using var context = await ContextAsync();
        var (runtime, owner, _) = await SeedAsync(context);
        var leases = new Mock<IDistributedLeaseProvider>();
        leases.Setup(item => item.AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromException<IDistributedLease>(new TimeoutException()));
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            Service(context, leaseProvider: leases.Object).DeleteRuntimeAsync(runtime.PublicId, owner, true, default));
        Assert.Equal("record_delete_conflict", error.Code);
        Assert.True(await context.TeamLabRuntimes.AnyAsync(item => item.Id == runtime.Id));
    }

    [Theory]
    [InlineData(CapacityReservationStatus.Active, false)]
    [InlineData(CapacityReservationStatus.Active, true)]
    [InlineData(CapacityReservationStatus.Confirmed, false)]
    public async Task OutstandingCapacityReservation_StillPreventsRecordDeletion(CapacityReservationStatus status, bool released)
    {
        await using var context = await ContextAsync();
        var (runtime, owner, node) = await SeedAsync(context);
        var ticket = new DeploymentQueueTicket { Kind = DeploymentQueueKind.TeamLabRuntime,
            TeamLabRuntimeId = runtime.Id, Status = DeploymentQueueTicketStatus.Succeeded };
        context.Add(new FleetCapacityReservation { DeploymentQueueTicket = ticket, WorkerNodeId = node,
            Status = status, ReleasedAt = released ? DateTimeOffset.UtcNow : null });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var executor = Executor();

        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            Service(context, executor).DeleteRuntimeAsync(runtime.PublicId, owner, true, default));

        Assert.Equal("runtime_cleanup_pending", error.Code);
        Assert.True(await context.TeamLabRuntimes.AnyAsync(item => item.Id == runtime.Id));
        Assert.Single(await context.FleetCapacityReservations.ToArrayAsync());
        executor.Verify(item => item.GetRuntimeInventoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<(TeamLabRuntime, Guid, Guid)> SeedAsync(AppDbContext context)
    {
        var owner = Guid.CreateVersion7();
        var node = Guid.CreateVersion7();
        context.Add(new UserInfo { Id = owner, UserName = "delete-owner", Role = Role.Admin });
        context.Add(new WorkerNode { Id = node, Name = "delete-node" });
        context.Add(new ImageTemplate { Id = 7, Name = "preserved-image", Status = ImageStatus.Ready, ImageType = ImageType.Qcow2 });
        var topology = new TeamLabTopology { Name = "delete-scene", OwnerUserId = owner };
        var draftNetwork = new TeamLabTopologyNetwork { Topology = topology, Key = "inside", Name = "Inside",
            AddressPoolCidr = "10.45.0.0/16", RuntimePrefixLength = 24 };
        topology.Networks.Add(draftNetwork);
        topology.Assets.Add(new TeamLabTopologyAsset { Topology = topology, Key = "vm", ImageTemplateId = 7,
            Interfaces = [new TeamLabTopologyInterface { Network = draftNetwork, Key = "nic", HostOffset = 10 }] });
        var release = new TeamLabTopologyRelease { Topology = topology, Version = 1, ContentHash = "test", CanonicalJson = "{}" };
        var runtime = new TeamLabRuntime { TopologyReleaseId = release.Id, CreatedById = owner, Status = TeamLabRuntimeStatus.Destroyed, UpdatedAt = DateTimeOffset.UtcNow };
        context.AddRange(release, runtime);
        await context.SaveChangesAsync();
        var shard = new TeamLabRuntimeShard { RuntimeId = runtime.Id, WorkerNodeId = node, Status = TeamLabRuntimeStatus.Destroyed };
        runtime.Shards.Add(shard);
        await context.SaveChangesAsync();
        runtime.EntryShardId = shard.Id;
        runtime.Assets.Add(new TeamLabRuntimeAsset { RuntimeId = runtime.Id, ShardId = shard.Id, WorkerNodeId = node,
            TopologyKey = "vm", Kind = TeamLabResourceKind.Vm, Status = TeamLabRuntimeStatus.Destroyed, RuntimeResourceId = $"gzctf-tl-{runtime.PublicId:N}-1-legacy-vm" });
        await context.SaveChangesAsync();
        return (runtime, owner, node);
    }

    private static Mock<ITeamLabNodeExecutor> Executor()
    {
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.GetRuntimeInventoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabNodeRuntimeInventory([], [], [], DateTimeOffset.UtcNow, true, true));
        return executor;
    }

    private static TeamLabReleaseImagePreparationService Preparation(AppDbContext context) => new(context,
        new ImageDistributionService(context, null!, null!, null!, null!, new ImageDistributionCoordinator(),
            new DeploymentExecutionContextAccessor(), Mock.Of<IOperationalEventWriter>(), NullLogger<ImageDistributionService>.Instance));

    private static TeamLabRecordDeletionService Service(AppDbContext context, Mock<ITeamLabNodeExecutor>? executor = null,
        Mock<IBlobStorage>? storage = null, ITeamLabUsageProjectionProvider? usage = null,
        IDistributedLeaseProvider? leaseProvider = null) => new(context,
            usage ?? new TeamLabEmptyUsageProjectionProvider(), (executor ?? Executor()).Object, Preparation(context),
            (storage ?? new Mock<IBlobStorage>()).Object, leaseProvider ?? new LocalDevelopmentLeaseProvider(),
            new TeamLabRuntimeOperationPayloadProtector(new EphemeralDataProtectionProvider()), new TeamLabRecordReferenceQuery(context),
            Mock.Of<IOperationalEventWriter>(), new TeamLabRecordReferenceQuery(context));
}
