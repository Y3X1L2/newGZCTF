using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.Identity.Contracts;
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

    [Fact]
    public async Task CleanupFailure_PersistsValidRetryableEvent_ThenEndsAfterRecovery()
    {
        await using var context = CreateContext();
        var session = (await SeedAsync(context, 1, expired: true))[0];
        var relay = new Mock<ITeamLabRemoteRelayGateway>();
        relay.SetupSequence(item => item.CancelTerminalAsync(session.WorkerNodeId, session.PublicId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("relay unavailable"))
            .Returns(Task.CompletedTask);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Service(context, relay.Object, cache,
            new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance));

        await service.ExpireAsync(default);

        Assert.Equal(TeamLabRemoteSessionStatus.Ending, session.Status);
        Assert.Equal("cleanup_pending", session.EndReason);
        Assert.Null(session.EndedAt);
        var failure = Assert.Single(await context.Set<OperationalEvent>().Where(item =>
            item.Outcome == OperationalEventOutcome.Failed).ToArrayAsync());
        Assert.Equal(OperationalErrorCategory.Unknown, failure.ErrorCategory);
        Assert.Equal(OperationalErrorCodes.RecoveryDeferred, failure.ErrorCode);
        Assert.True(failure.Retryable);

        await service.ExpireAsync(default);

        Assert.Equal(TeamLabRemoteSessionStatus.Ended, session.Status);
        Assert.NotNull(session.EndedAt);
        Assert.Contains(await context.Set<OperationalEvent>().ToArrayAsync(), item =>
            item.EventCode == OperationalEventCodes.TeamLab.RemoteSessionEnded && item.Outcome == OperationalEventOutcome.Succeeded);
    }

    [Fact]
    public async Task AccountRevocation_DeniesAllActiveSessionsBeforeExternalCleanup_AndKeepsOtherUsers()
    {
        await using var context = CreateContext();
        var sessions = await SeedAsync(context, 4, expired: false);
        sessions[0].Status = TeamLabRemoteSessionStatus.Creating;
        sessions[1].Status = TeamLabRemoteSessionStatus.Ready;
        var other = new UserInfo();
        context.Users.Add(other);
        sessions[3].RequestedByUserId = other.Id;
        await context.SaveChangesAsync();
        var relay = new Mock<ITeamLabRemoteRelayGateway>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var participant = (IUserAccountRevocationParticipant)Service(context, relay.Object, cache);

        await participant.PrepareAsync(sessions[0].RequestedByUserId, default);

        Assert.All(sessions.Take(3), session =>
        {
            Assert.Equal(TeamLabRemoteSessionStatus.Ending, session.Status);
            Assert.True(session.ExpiresAt <= DateTimeOffset.UtcNow);
            Assert.Equal("account_revoked", session.EndReason);
        });
        Assert.Equal(TeamLabRemoteSessionStatus.Connected, sessions[3].Status);
        relay.VerifyNoOtherCalls();

        await participant.CompleteAsync(sessions[0].RequestedByUserId, default);

        Assert.All(sessions.Take(3), session => Assert.Equal(TeamLabRemoteSessionStatus.Ended, session.Status));
        relay.Verify(item => item.CancelTerminalAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        await participant.CompleteAsync(sessions[0].RequestedByUserId, default);
        relay.Verify(item => item.CancelTerminalAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task AccountRevocation_PreservesFailedCleanupForTheExistingWorkerToRetry()
    {
        await using var context = CreateContext();
        var session = (await SeedAsync(context, 1, expired: false))[0];
        var relay = new Mock<ITeamLabRemoteRelayGateway>();
        relay.SetupSequence(item => item.CancelTerminalAsync(session.WorkerNodeId, session.PublicId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("node offline"))
            .Returns(Task.CompletedTask);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Service(context, relay.Object, cache);
        var participant = (IUserAccountRevocationParticipant)service;
        await participant.PrepareAsync(session.RequestedByUserId, default);
        await participant.CompleteAsync(session.RequestedByUserId, default);
        Assert.Equal(TeamLabRemoteSessionStatus.Ending, session.Status);
        Assert.Equal("cleanup_pending", session.EndReason);
        await service.ExpireAsync(default);
        Assert.Equal(TeamLabRemoteSessionStatus.Ended, session.Status);
    }

    [Fact]
    public async Task AccountRevocation_WorkerRecoversACommittedDeletionWithoutThePostCommitCallback()
    {
        await using var context = CreateContext();
        var session = (await SeedAsync(context, 1, expired: false))[0];
        (await context.Users.SingleAsync(item => item.Id == session.RequestedByUserId)).Role = Role.Banned;
        await context.SaveChangesAsync();
        var relay = new Mock<ITeamLabRemoteRelayGateway>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await Service(context, relay.Object, cache).ExpireAsync(default);
        Assert.Equal(TeamLabRemoteSessionStatus.Ended, session.Status);
        Assert.Equal("account_revoked", session.EndReason);
        relay.Verify(item => item.CancelTerminalAsync(session.WorkerNodeId, session.PublicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOperation_DoesNotReplayAReadySessionForADeletedOrBannedAccount()
    {
        await using var context = CreateContext();
        var session = (await SeedAsync(context, 1, expired: false))[0];
        session.Status = TeamLabRemoteSessionStatus.Ready;
        (await context.Users.SingleAsync(item => item.Id == session.RequestedByUserId)).Role = Role.Banned;
        await context.SaveChangesAsync();
        var relay = new Mock<ITeamLabRemoteRelayGateway>(MockBehavior.Strict);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            Service(context, relay.Object, cache).CreateForOperationAsync(session.Runtime.PublicId,
                session.RuntimeAssetId, session.RequestedByUserId, session.Reason, session.PublicId, default));
        Assert.Equal("remote_access_account_unavailable", error.Code);
        relay.VerifyNoOtherCalls();
    }

    private static TeamLabRemoteAccessService Service(AppDbContext context, ITeamLabRemoteRelayGateway relay, IMemoryCache cache,
        IOperationalEventWriter? writer = null) =>
        new(context, new TeamLabAuthorizationService(context),
            new TeamLabScopeAuthorizationService(context), relay, null!, null!,
            new TeamLabEventRecorder(context, writer ?? Mock.Of<IOperationalEventWriter>(), new OperationalCorrelation()),
            cache, NullLogger<TeamLabRemoteAccessService>.Instance);

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<TeamLabRemoteSession[]> SeedAsync(AppDbContext context, int count, bool expired)
    {
        var runtime = new TeamLabRuntime();
        var asset = new TeamLabRuntimeAsset { Runtime = runtime, Name = "terminal" };
        var actor = new UserInfo();
        context.Users.Add(actor);
        var node = Guid.NewGuid();
        var sessions = Enumerable.Range(1, count).Select(id => new TeamLabRemoteSession
        {
            Id = id, Runtime = runtime, RuntimeAsset = asset, WorkerNodeId = node, RequestedByUserId = actor.Id,
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
