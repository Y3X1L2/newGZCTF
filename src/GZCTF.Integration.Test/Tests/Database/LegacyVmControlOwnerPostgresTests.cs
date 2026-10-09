using GZCTF.Models;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.Runtime.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.Repositories;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class LegacyVmControlOwnerPostgresTests : IAsyncLifetime
{
    readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("legacy_owner").WithUsername("postgres").WithPassword("postgres").WithCleanUp(true).Build();
    public async Task InitializeAsync() => await postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(3, true, false)]
    [InlineData(3, false, true)]
    public async Task LegacyDestroyUsesProvenOwnerWithoutNewCapacity(int generation, bool historicalRemote, bool blocked)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await context.Database.EnsureCreatedAsync();
        var user = new UserInfo { UserName = "legacy-fixture", Role = Role.Admin };
        var game = new Game { Title = "legacy fixture" };
        var challenge = new GameChallenge { Title = "legacy fixture", Game = game };
        var local = new WorkerNode { Name = "local", IsLocal = true, Status = NodeStatus.Online, Capabilities = NodeCapability.Kvm };
        var remote = new WorkerNode { Name = "remote", Status = NodeStatus.Online, Capabilities = NodeCapability.Kvm };
        context.AddRange(user, challenge, local, remote);
        await context.SaveChangesAsync();
        var vm = new VmInstance { UserId = user.Id, ChallengeId = challenge.Id, ProviderName = "KVM",
            VmName = $"vm_c{challenge.Id}_u{user.Id}", RuntimeGeneration = generation, Status = VmInstanceStatus.Error };
        context.Add(vm); await context.SaveChangesAsync();
        var request = DeploymentQueueRequest.Vm(game.Id, user.Id, challenge.Id, vm.Id);
        if (historicalRemote)
        {
            var history = DeploymentQueueTicket.Create(request with { Generation = generation, TargetNodeId = remote.Id });
            history.Status = DeploymentQueueTicketStatus.Failed; context.Add(history);
        }
        var ticket = DeploymentQueueTicket.Create(request with { Generation = generation, Operation = RuntimeOperationKind.Destroy });
        context.Add(ticket); await context.SaveChangesAsync();
        // The scheduler runs in a fresh production scope; SQL claims must not read a seed entity
        // cached before ExecuteUpdate changed its claim owner and concurrency version.
        context.ChangeTracker.Clear();
        var lease = new LocalDevelopmentLeaseProvider();
        var options = Options.Create(new RuntimeSchedulingOptions());
        var snapshots = new NodeCapacitySnapshotService(context);
        var eligibility = new NodeEligibilityEvaluator(options);
        var events = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var correlation = new OperationalCorrelation();
        var scheduler = new RuntimeSchedulingService(context,
            new FleetCapacityReservationService(context, lease, snapshots, eligibility, events, NullLogger<FleetCapacityReservationService>.Instance),
            new RuntimeQueueSelector(context, options),
            new TeamLabPhysicalPlacementService(context, lease, snapshots, eligibility, events, new TeamLabEventRecorder(context, events, correlation),
                new TeamLabFabricLinkAllocator(context, Options.Create(new TeamLabNetworkConfig())), Options.Create(new TeamLabNetworkConfig()), options),
            new PollingDeploymentQueueWakeup(), events, correlation, Options.Create(new KvmSettings()), NullLogger<RuntimeSchedulingService>.Instance);
        Assert.Equal(blocked ? 0 : 1, await scheduler.SchedulePendingAsync(default));
        var stored = await context.DeploymentQueueTickets.AsNoTracking().SingleAsync(item => item.Id == ticket.Id);
        Assert.Equal(blocked ? null : historicalRemote ? remote.Id : local.Id, stored.TargetNodeId);
        Assert.Equal(blocked ? DeploymentQueueTicketStatus.Pending : DeploymentQueueTicketStatus.Scheduled, stored.Status);
        Assert.Empty(await context.FleetCapacityReservations.ToArrayAsync());
    }

    [Theory]
    [InlineData(true, true, VmInstanceStatus.Running, false, false, 0, true)]
    [InlineData(true, true, VmInstanceStatus.Error, false, true, 0, true)]
    [InlineData(true, true, VmInstanceStatus.Error, true, false, 2, true)]
    [InlineData(false, true, VmInstanceStatus.Error, true, false, 2, false)]
    [InlineData(false, false, VmInstanceStatus.Error, true, false, 2, false)]
    [InlineData(false, true, VmInstanceStatus.Error, false, false, 0, false)]
    [InlineData(false, true, VmInstanceStatus.Error, true, false, 1, false)]
    [InlineData(false, true, VmInstanceStatus.Destroyed, false, true, 2, false)]
    public async Task SameHostOtherWorkerRuntimeEvidenceFencesLegacyDestroyInPostgres(
        bool localTarget, bool sameHost, VmInstanceStatus otherStatus, bool missingOwner, bool nativeId,
        int dispatchedGeneration, bool blocked)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var user = new UserInfo { UserName = "host-" + Guid.NewGuid().ToString("N")[..8], Role = Role.Admin };
        var game = new Game { Title = "same host fixture" };
        var challenge = new GameChallenge { Title = "same host fixture", Game = game };
        var owner = new WorkerNode { Name = "legacy-owner", HostAddress = "192.0.2.27", IsLocal = localTarget,
            Status = NodeStatus.Online, Capabilities = NodeCapability.Kvm };
        var managed = new WorkerNode { Name = "managed-worker", HostAddress = sameHost ? "::ffff:192.0.2.27" : "192.0.2.31",
            Status = NodeStatus.Online, Capabilities = NodeCapability.Kvm };
        context.AddRange(user, challenge, owner, managed);
        await context.SaveChangesAsync();
        var legacy = new VmInstance { UserId = user.Id, ChallengeId = challenge.Id, ProviderName = "KVM",
            VmName = $"vm_c{challenge.Id}_u{user.Id}", RuntimeGeneration = 1, Status = VmInstanceStatus.Error };
        var other = new VmInstance { UserId = user.Id, ChallengeId = challenge.Id, ProviderName = "KVM",
            VmName = legacy.VmName, NodeId = missingOwner ? null : managed.Id, RuntimeGeneration = 2,
            Status = otherStatus, RuntimeNativeId = nativeId ? Guid.NewGuid().ToString() : null };
        context.AddRange(legacy, other);
        if (dispatchedGeneration > 0)
        {
            var history = DeploymentQueueTicket.Create(DeploymentQueueRequest.Vm(game.Id, user.Id, challenge.Id, other.Id) with
            { Generation = dispatchedGeneration, TargetNodeId = managed.Id });
            history.Status = DeploymentQueueTicketStatus.Failed;
            context.Add(history);
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var events = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        var agent = new RecordingDestroyAgent();
        var accessor = new DeploymentExecutionContextAccessor();
        var fleet = new FleetVmService(agent, new NodeRepository(context, events), null!, null!, null!, null!,
            Options.Create(new KvmSettings()), context, null!, accessor, NullLogger<FleetVmService>.Instance);
        var execution = new DeploymentExecutionService(context, fleet, accessor, NullLogger<DeploymentExecutionService>.Instance);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.Vm(game.Id, user.Id, challenge.Id, legacy.Id) with
        { Operation = RuntimeOperationKind.Destroy, Generation = 1, TargetNodeId = owner.Id });

        if (blocked)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(ticket, default));
            Assert.Contains("Another VM generation", error.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.True((await execution.ExecuteAsync(ticket, default)).Success);
            // RuntimeExecutionService persists the tracked VM with the ticket's terminal commit.
            await context.SaveChangesAsync();
        }

        await using var verify = new AppDbContext(options);
        var stored = await verify.VmInstances.SingleAsync(item => item.Id == legacy.Id);
        Assert.Equal(blocked ? VmInstanceStatus.Error : VmInstanceStatus.Destroyed, stored.Status);
        Assert.Equal(blocked ? null : (Guid?)owner.Id, stored.NodeId);
        Assert.Equal(blocked, stored.DestroyedAt is null);
        var retained = await verify.VmInstances.SingleAsync(item => item.Id == other.Id);
        Assert.Equal(otherStatus, retained.Status);
        Assert.Equal(other.RuntimeNativeId, retained.RuntimeNativeId);
        Assert.Equal(blocked ? null : (Guid?)owner.Id, agent.NodeId);
        Assert.Empty(await verify.FleetCapacityReservations.ToArrayAsync());
        Assert.Null(accessor.Current);
    }

    sealed class RecordingDestroyAgent() : AgentClient(null!, null!, new ConfigurationBuilder().Build(), NullLogger<AgentClient>.Instance)
    {
        public Guid? NodeId;
        public override Task DestroyVmAsync(Guid nodeId, string name, int? generation, string? nativeId, CancellationToken token)
        { NodeId = nodeId; return Task.CompletedTask; }
    }
}
