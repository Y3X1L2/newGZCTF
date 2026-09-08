using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRemoteReconciliationTests
{
    [Theory]
    [InlineData(false, TeamLabRemoteSessionStatus.Ended)]
    [InlineData(true, TeamLabRemoteSessionStatus.Ending)]
    public async Task ConsoleCleanupOnlyRequiresGuacamoleAfterCreationStarted(bool started, TeamLabRemoteSessionStatus expected)
    {
        await using var context = CreateContext();
        var session = (await SeedAsync(context, 1, true))[0];
        session.Protocol = TeamLabRemoteProtocol.Vnc;
        session.Status = TeamLabRemoteSessionStatus.Ending;
        session.GuacamoleCreationStarted = started;
        await context.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await Service(context, Mock.Of<ITeamLabRemoteRelayGateway>(), cache).ExpireAsync(default);
        Assert.Equal(expected, session.Status);
    }

    [Fact]
    public async Task CreateOperation_ReplaysReadySessionWithoutCreatingResources()
    {
        await using var context = CreateContext();
        var session = (await SeedAsync(context, 1, false))[0];
        session.Status = TeamLabRemoteSessionStatus.Ready;
        session.Runtime.Status = TeamLabRuntimeStatus.Running;
        session.Generation = session.Runtime.Generation;
        session.Reason = "test access";
        session.Runtime.CreatedById = session.RequestedByUserId;
        await context.SaveChangesAsync();
        var relay = new Mock<ITeamLabRemoteRelayGateway>(MockBehavior.Strict);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var result = await Service(context, relay.Object, cache).CreateForOperationAsync(
            session.Runtime.PublicId, session.RuntimeAssetId, session.RequestedByUserId,
            session.Reason, session.PublicId, default);

        Assert.Equal(session.PublicId, result.Id);
        Assert.Equal(1, await context.TeamLabRemoteSessions.CountAsync());
        relay.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateOperation_InterruptedCreationIsCleanedWithoutReplacement()
    {
        await using var context = CreateContext();
        var session = (await SeedAsync(context, 1, false))[0];
        session.Status = TeamLabRemoteSessionStatus.Creating;
        session.Reason = "test access";
        session.Runtime.CreatedById = session.RequestedByUserId;
        await context.SaveChangesAsync();
        var relay = new Mock<ITeamLabRemoteRelayGateway>();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            Service(context, relay.Object, cache).CreateForOperationAsync(session.Runtime.PublicId,
                session.RuntimeAssetId, session.RequestedByUserId, session.Reason, session.PublicId, default));

        Assert.Equal("remote_session_creation_interrupted", error.Code);
        Assert.Equal(TeamLabRemoteSessionStatus.Ended, session.Status);
        Assert.Equal(1, await context.TeamLabRemoteSessions.CountAsync());
        relay.Verify(item => item.CancelTerminalAsync(session.WorkerNodeId, session.PublicId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateOperation_DoesNotReplayAnExpiredOrStaleGeneration(bool expired)
    {
        await using var context = CreateContext();
        var session = (await SeedAsync(context, 1, expired))[0];
        session.Status = TeamLabRemoteSessionStatus.Ready;
        session.Runtime.Status = TeamLabRuntimeStatus.Running;
        session.Generation = session.Runtime.Generation - (expired ? 0 : 1);
        session.Reason = "test access";
        session.Runtime.CreatedById = session.RequestedByUserId;
        await context.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            Service(context, Mock.Of<ITeamLabRemoteRelayGateway>(), cache).CreateForOperationAsync(
                session.Runtime.PublicId, session.RuntimeAssetId, session.RequestedByUserId,
                session.Reason, session.PublicId, default));

        Assert.Equal(TeamLabRemoteSessionStatus.Ended, session.Status);
        Assert.Equal(1, await context.TeamLabRemoteSessions.CountAsync());
    }

    [Fact]
    public async Task Reconcile_VisitsSessionsBeyondFirstInventoryBatch()
    {
        await using var context = CreateContext();
        var sessions = await SeedAsync(context, 501, expired: false);
        var relay = new Mock<ITeamLabRemoteRelayGateway>();
        relay.Setup(item => item.InventoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions.Select(item => item.PublicId).ToArray());
        using var cache = new MemoryCache(new MemoryCacheOptions());

        await Service(context, relay.Object, cache).ExpireAsync(default);

        relay.Verify(item => item.InventoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.All(sessions, item => Assert.Equal(TeamLabRemoteSessionStatus.Connected, item.Status));
    }

    [Fact]
    public async Task Expire_FailedFirstBatchDoesNotStarveLaterSessions()
    {
        await using var context = CreateContext();
        var sessions = await SeedAsync(context, 101, expired: true);
        var relay = new Mock<ITeamLabRemoteRelayGateway>();
        relay.Setup(item => item.CancelTerminalAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, Guid, CancellationToken>((_, id, _) => id == sessions[^1].PublicId
                ? Task.CompletedTask : Task.FromException(new InvalidOperationException("node unavailable")));
        using var cache = new MemoryCache(new MemoryCacheOptions());

        await Service(context, relay.Object, cache).ExpireAsync(default);

        Assert.All(sessions.Take(100), item => Assert.Equal(TeamLabRemoteSessionStatus.Ending, item.Status));
        Assert.Equal(TeamLabRemoteSessionStatus.Ended, sessions[^1].Status);
        relay.Verify(item => item.CancelTerminalAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Exactly(101));
    }

    private static TeamLabRemoteAccessService Service(AppDbContext context, ITeamLabRemoteRelayGateway relay, IMemoryCache cache) =>
        new(context, new TeamLabRemoteAccessAuthorizationService(new TeamLabAuthorizationService(context, [], [])), relay, null!, null!,
            new TeamLabEventRecorder(context, Mock.Of<IOperationalEventWriter>(), new OperationalCorrelation()),
            cache, NullLogger<TeamLabRemoteAccessService>.Instance);

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<TeamLabRemoteSession[]> SeedAsync(AppDbContext context, int count, bool expired)
    {
        var runtime = new TeamLabRuntime();
        var asset = new TeamLabRuntimeAsset { Runtime = runtime, Name = "terminal" };
        var node = Guid.NewGuid();
        var sessions = Enumerable.Range(1, count).Select(id => new TeamLabRemoteSession
        {
            Id = id, Runtime = runtime, RuntimeAsset = asset, WorkerNodeId = node,
            Protocol = TeamLabRemoteProtocol.ContainerTerminal,
            Status = TeamLabRemoteSessionStatus.Connected,
            ConnectedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 20)
        }).ToArray();
        context.TeamLabRemoteSessions.AddRange(sessions);
        await context.SaveChangesAsync();
        return sessions;
    }
}
