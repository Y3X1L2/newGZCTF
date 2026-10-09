using GZCTF.Models;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.Runtime.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
}
