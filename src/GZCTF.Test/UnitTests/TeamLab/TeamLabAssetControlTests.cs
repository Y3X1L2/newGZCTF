using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Utils;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabAssetControlTests
{
    [Fact]
    public async Task RestartContinuesFromAcknowledgedStopWithoutStoppingTwice()
    {
        await using var fixture = await Fixture.Create("restart");
        fixture.Ticket.TargetNodeId = null; // TeamLab control-plane scheduling deliberately has no target node.
        fixture.Gateway.SetupSequence(item => item.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<TeamLabAssetControlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabAssetControlResult(true, null, fixture.Fact("exited")))
            .ReturnsAsync(new TeamLabAssetControlResult(false, "asset_control.execution_failed", fixture.Fact("exited")))
            .ReturnsAsync(new TeamLabAssetControlResult(true, null, fixture.Fact("running")));
        Assert.False((await fixture.Service.ExecuteAsync(fixture.Ticket, default)).Success);
        Assert.Equal(1, fixture.Protector.Unprotect(fixture.Ticket.ProtectedPayload!).AssetControl!.Phase);
        Assert.Equal(TeamLabRuntimeStatus.Stopped, fixture.Asset.Status);
        Assert.True((await fixture.Service.ExecuteAsync(fixture.Ticket, default)).Success);
        fixture.Gateway.Verify(item => item.ExecuteAsync(It.IsAny<Guid>(), It.Is<TeamLabAssetControlRequest>(request => request.Action == "stop"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(TeamLabRuntimeStatus.Running, fixture.Asset.Status);
        Assert.Equal("peer-resource", fixture.Peer.RuntimeResourceId);
    }

    [Fact]
    public async Task RebuildOnlyReplacesSelectedAssetAndPersistsNewIdentity()
    {
        await using var fixture = await Fixture.Create("rebuild");
        fixture.Gateway.SetupSequence(item => item.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<TeamLabAssetControlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabAssetControlResult(true, null, null))
            .ReturnsAsync(new TeamLabAssetControlResult(true, null, fixture.Fact("running") with { ResourceId = "new-web" }));
        Assert.True((await fixture.Service.ExecuteAsync(fixture.Ticket, default)).Success);
        Assert.Equal("new-web", fixture.Asset.RuntimeResourceId);
        Assert.Equal("peer-resource", fixture.Peer.RuntimeResourceId);
        fixture.Gateway.Verify(item => item.ExecuteAsync(It.IsAny<Guid>(), It.Is<TeamLabAssetControlRequest>(request => request.AssetKey != "web"), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(2, fixture.Protector.Unprotect(fixture.Ticket.ProtectedPayload!).AssetControl!.Phase);
    }

    [Fact]
    public async Task IdentityConflictNeverAdoptsForeignAgentResource()
    {
        await using var fixture = await Fixture.Create("stop");
        fixture.Gateway.Setup(item => item.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<TeamLabAssetControlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabAssetControlResult(false, "asset_control.identity_conflict", fixture.Fact("running") with { ResourceId = "foreign" }));
        Assert.False((await fixture.Service.ExecuteAsync(fixture.Ticket, default)).Success);
        Assert.Equal("old-web", fixture.Asset.RuntimeResourceId);
        Assert.Equal(0, fixture.Protector.Unprotect(fixture.Ticket.ProtectedPayload!).AssetControl!.Phase);
    }

    [Fact]
    public async Task SuccessWithoutExpectedInventoryDoesNotCompleteStep()
    {
        await using var fixture = await Fixture.Create("stop");
        fixture.Gateway.Setup(item => item.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<TeamLabAssetControlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabAssetControlResult(true, null, fixture.Fact("running")));
        Assert.False((await fixture.Service.ExecuteAsync(fixture.Ticket, default)).Success);
        Assert.Equal("asset_control.state_not_reached", fixture.Asset.LastError);
    }

    [Fact]
    public async Task ReboundAssetAndStaleGenerationAreRejectedBeforeAgentExecution()
    {
        await using var fixture = await Fixture.Create("stop");
        fixture.Asset.RuntimeResourceId = "rebound";
        await fixture.Db.SaveChangesAsync();
        Assert.False((await fixture.Service.ExecuteAsync(fixture.Ticket, default)).Success);
        fixture.Gateway.VerifyNoOtherCalls();
        fixture.Asset.Generation++;
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<TeamLabApiContractException>(() => fixture.Service.ExecuteAsync(fixture.Ticket, default));
        fixture.Gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(RuntimeOperationKind.AssetControl, true)]
    [InlineData(RuntimeOperationKind.Reset, false)]
    public void QueuePayloadRetentionPreservesOnlyAssetControlCheckpoint(RuntimeOperationKind operation, bool retained)
    {
        var ticket = new DeploymentQueueTicket { Operation = operation, ProtectedPayload = "encrypted-checkpoint" };
        ticket.ReleaseTransientPayload();
        Assert.Equal(retained, ticket.ProtectedPayload is not null);
    }

    sealed class Fixture : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public TeamLabRuntimeAsset Asset { get; private set; } = null!;
        public TeamLabRuntimeAsset Peer { get; private set; } = null!;
        public DeploymentQueueTicket Ticket { get; private set; } = null!;
        public TeamLabRuntimeOperationPayloadProtector Protector { get; } = new(new EphemeralDataProtectionProvider());
        public Mock<ITeamLabAssetControlGateway> Gateway { get; } = new(MockBehavior.Strict);
        public TeamLabAssetControlService Service { get; private set; } = null!;
        public TeamLabExecutionInventoryFactV2 Fact(string state) => new("docker", "web", "old-web", state, 3);

        public static async Task<Fixture> Create(string action)
        {
            var fixture = new Fixture();
            var db = fixture.Db;
            var actor = new UserInfo { UserName = "fixture", Role = Role.Admin };
            var runtime = new TeamLabRuntime { Generation = 3, Status = TeamLabRuntimeStatus.Running, CreatedById = actor.Id };
            var shard = new TeamLabRuntimeShard { Runtime = runtime, Generation = 3, WorkerNodeId = Guid.NewGuid() };
            fixture.Asset = new TeamLabRuntimeAsset { Runtime = runtime, Shard = shard, WorkerNodeId = shard.WorkerNodeId,
                Generation = 3, Kind = TeamLabResourceKind.Docker, TopologyKey = "web", Name = "Web", RuntimeResourceId = "old-web", Status = TeamLabRuntimeStatus.Running };
            fixture.Peer = new TeamLabRuntimeAsset { Runtime = runtime, Shard = shard, WorkerNodeId = shard.WorkerNodeId,
                Generation = 3, Kind = TeamLabResourceKind.Docker, TopologyKey = "peer", Name = "Peer", RuntimeResourceId = "peer-resource", Status = TeamLabRuntimeStatus.Running };
            db.Users.Add(actor);
            db.TeamLabRuntimeAssets.AddRange(fixture.Asset, fixture.Peer);
            await db.SaveChangesAsync();
            var plan = new TeamLabExecutionPlanV2(runtime.Id, runtime.PublicId, 3, "shard", "", "sha256:" + new string('a', 64), false, [],
                [new("web", "docker", "web", "sha256:" + new string('a', 64), null, 1, 1, 128, [], []),
                 new("peer", "docker", "peer", "sha256:" + new string('a', 64), null, 1, 1, 128, [], [])], []);
            plan = plan with { PlanDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan))) };
            Assert.True(plan.IsValid(out var planError), planError);
            db.TeamLabExecutionPlanSnapshots.Add(new() { Runtime = runtime, Shard = shard, Generation = 3, WorkerNodeId = shard.WorkerNodeId,
                PlanDigest = plan.PlanDigest, PlanJson = JsonSerializer.Serialize(plan) });
            fixture.Ticket = new DeploymentQueueTicket { Kind = DeploymentQueueKind.TeamLabRuntime, Operation = RuntimeOperationKind.AssetControl,
                Generation = 3, TeamLabRuntimeId = runtime.Id, TargetNodeId = shard.WorkerNodeId, OwnerUserId = actor.Id, Status = DeploymentQueueTicketStatus.Running,
                ProtectedPayload = fixture.Protector.Protect(new(null, runtime.PublicId, null) { AssetControl = new(fixture.Asset.Id, new(3, action, "test asset operation", true), "old-web", null, WorkerNodeId: shard.WorkerNodeId) }) };
            db.DeploymentQueueTickets.Add(fixture.Ticket);
            await db.SaveChangesAsync();
            fixture.Service = new(db, new TeamLabAuthorizationService(db, [], []), new TeamLabRuntimeLifecycleGuard(db), Mock.Of<ITeamLabRuntimeQueue>(), fixture.Protector,
                fixture.Gateway.Object, Mock.Of<ITeamLabRemoteAccessService>(), new TeamLabEventRecorder(db,
                    new EfOperationalEventWriter(db, NullLogger<EfOperationalEventWriter>.Instance), new OperationalCorrelation()), new LocalDevelopmentLeaseProvider());
            return fixture;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
