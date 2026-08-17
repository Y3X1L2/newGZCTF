using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Penetration.Application;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class PenetrationTeamLabLifecycleTests
{
    [Fact]
    public async Task PendingReset_PreventsASecondQuotaReservation()
    {
        var database = new TestDatabase();
        await database.SeedAsync(maxResetCount: 1);
        var enteredQueue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQueue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimes = RuntimeMock();
        runtimes.Setup(item => item.ResetAndEnqueueAsync(
                It.IsAny<Guid>(), It.IsAny<ResetTeamLabRuntimeModel>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Guid runtimeId, ResetTeamLabRuntimeModel _, Guid? _, CancellationToken token) =>
            {
                enteredQueue.SetResult();
                await releaseQueue.Task.WaitAsync(token);
                return new TeamLabRuntimeCreateResult(31, runtimeId, false);
            });

        await using var firstContext = database.CreateContext();
        var firstAdapter = Adapter(firstContext, runtimes.Object);
        var firstReset = firstAdapter.ResetTeamAsync(7, 11, Guid.NewGuid(), false, CancellationToken.None);
        await enteredQueue.Task;

        await using var secondContext = database.CreateContext();
        var secondAdapter = Adapter(secondContext, runtimes.Object);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            secondAdapter.ResetTeamAsync(7, 11, Guid.NewGuid(), false, CancellationToken.None));
        Assert.Contains("already pending", error.Message, StringComparison.OrdinalIgnoreCase);

        releaseQueue.SetResult();
        await firstReset;
        await using var verification = database.CreateContext();
        Assert.Single(await verification.PenetrationResetRecords.ToArrayAsync());
    }

    [Fact]
    public async Task EnqueueInfrastructureFailure_ReleasesResetQuota()
    {
        var database = new TestDatabase();
        await database.SeedAsync(maxResetCount: 1);
        var runtimes = RuntimeMock();
        runtimes.SetupSequence(item => item.ResetAndEnqueueAsync(
                It.IsAny<Guid>(), It.IsAny<ResetTeamLabRuntimeModel>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("queue unavailable"))
            .ReturnsAsync(new TeamLabRuntimeCreateResult(31, Guid.NewGuid(), false));

        await using (var firstContext = database.CreateContext())
        {
            var firstAdapter = Adapter(firstContext, runtimes.Object);
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                firstAdapter.ResetTeamAsync(7, 11, Guid.NewGuid(), false, CancellationToken.None));
        }

        await using (var secondContext = database.CreateContext())
        {
            var secondAdapter = Adapter(secondContext, runtimes.Object);
            await secondAdapter.ResetTeamAsync(7, 11, Guid.NewGuid(), false, CancellationToken.None);
        }

        await using var verification = database.CreateContext();
        var records = await verification.PenetrationResetRecords.OrderBy(item => item.ResetAt).ToArrayAsync();
        Assert.Equal(2, records.Length);
        Assert.Equal(PenetrationResetFailureClass.Infrastructure, records[0].FailureClass);
        Assert.Equal(PenetrationResetStatus.Failed, records[0].Status);
        Assert.Equal(PenetrationResetStatus.Pending, records[1].Status);
    }

    [Fact]
    public async Task DestroyBinding_RemainsOwnedUntilPhysicalCleanupSucceeds()
    {
        var database = new TestDatabase();
        await database.SeedAsync(maxResetCount: 1);
        var runtimes = RuntimeMock();
        var operationIds = new List<Guid?>();
        runtimes.Setup(item => item.DestroyAndEnqueueAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? operation, Guid? _, CancellationToken _) =>
            {
                operationIds.Add(operation);
                return new TeamLabQueueTicketResult(Guid.NewGuid());
            });

        await using var context = database.CreateContext();
        var adapter = Adapter(context, runtimes.Object);
        await adapter.DestroyTeamAsync(7, 11, CancellationToken.None);
        await adapter.DestroyTeamAsync(7, 11, CancellationToken.None);
        var binding = await context.PenetrationTeamRuntimeBindings.SingleAsync();
        Assert.Equal(PenetrationRuntimeBindingStatus.Destroying, binding.Status);
        Assert.Equal(2, operationIds.Count);
        Assert.All(operationIds, operationId => Assert.Equal(binding.DestroyOperationId, operationId));

        var observer = new PenetrationTeamLabLifecycleObserver(context);
        await observer.ProjectAsync(new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.TeamLabRuntime,
            Operation = RuntimeOperationKind.Destroy,
            Status = DeploymentQueueTicketStatus.Failed,
            ApiOperationId = binding.DestroyOperationId,
            ErrorCategory = OperationalErrorCategory.NodeUnavailable
        }, CancellationToken.None);
        Assert.Equal(PenetrationRuntimeBindingStatus.Destroying, binding.Status);

        var runtime = await context.TeamLabRuntimes.SingleAsync();
        runtime.Status = TeamLabRuntimeStatus.Destroyed;
        await context.SaveChangesAsync();
        var completedAt = DateTimeOffset.UtcNow;
        await observer.ProjectAsync(new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.TeamLabRuntime,
            Operation = RuntimeOperationKind.Destroy,
            Status = DeploymentQueueTicketStatus.Succeeded,
            ApiOperationId = binding.DestroyOperationId,
            CompletedAt = completedAt
        }, CancellationToken.None);
        Assert.Equal(PenetrationRuntimeBindingStatus.Destroyed, binding.Status);
        Assert.Equal(completedAt, binding.DestroyedAt);
    }

    [Theory]
    [InlineData(OperationalErrorCategory.Docker)]
    [InlineData(OperationalErrorCategory.Kvm)]
    [InlineData(OperationalErrorCategory.Network)]
    [InlineData(OperationalErrorCategory.HealthCheck)]
    public async Task InfrastructureExecutionFailure_ReleasesResetQuota(
        OperationalErrorCategory category)
    {
        var database = new TestDatabase();
        await database.SeedAsync(maxResetCount: 1);
        var operationId = Guid.CreateVersion7();
        await using var context = database.CreateContext();
        context.PenetrationResetRecords.Add(new PenetrationResetRecord
        {
            RuntimeId = 31,
            UserId = Guid.NewGuid(),
            OperationId = operationId,
            TargetGeneration = 3,
            Status = PenetrationResetStatus.Running
        });
        await context.SaveChangesAsync();

        var observer = new PenetrationTeamLabLifecycleObserver(context);
        await observer.ProjectAsync(new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.TeamLabRuntime,
            Operation = RuntimeOperationKind.Reset,
            Status = DeploymentQueueTicketStatus.Failed,
            ApiOperationId = operationId,
            ErrorCategory = category
        }, CancellationToken.None);

        var record = await context.PenetrationResetRecords.SingleAsync();
        Assert.Equal(PenetrationResetStatus.Failed, record.Status);
        Assert.Equal(PenetrationResetFailureClass.Infrastructure, record.FailureClass);
    }

    static Mock<ITeamLabRuntimeApplicationService> RuntimeMock()
    {
        var runtime = new Mock<ITeamLabRuntimeApplicationService>(MockBehavior.Strict);
        runtime.Setup(item => item.GetByStorageIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, CancellationToken _) => new TeamLabRuntimeProjectionModel(
                Guid.NewGuid(), Guid.NewGuid(), 2, GZCTF.TeamLab.Contracts.TeamLabExecutionModel.V2, TeamLabRuntimeStatus.Running, "running", false,
                [], [], [], DateTimeOffset.UtcNow, null, null));
        return runtime;
    }

    static PenetrationTeamLabAdapter Adapter(
        AppDbContext context,
        ITeamLabRuntimeApplicationService runtimes) =>
        new(context, runtimes,
            Mock.Of<ITeamLabTopologyApplicationService>(),
            Mock.Of<ITeamLabControlPlaneOperationService>(),
            new PenetrationObjectiveService(context, null!, null!, null!, null!, null!));

    sealed class TestDatabase
    {
        readonly string _name = Guid.NewGuid().ToString("N");
        readonly InMemoryDatabaseRoot _root = new();

        public AppDbContext CreateContext() => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(_name, _root)
                .Options);

        public async Task SeedAsync(int maxResetCount)
        {
            await using var context = CreateContext();
            var releaseId = Guid.NewGuid();
            var runtime = new TeamLabRuntime
            {
                Id = 31,
                PublicId = Guid.NewGuid(),
                TopologyReleaseId = releaseId,
                Generation = 2,
                Status = TeamLabRuntimeStatus.Running,
                CreateRequestHash = "lifecycle-test"
            };
            context.TeamLabTopologyReleases.Add(new TeamLabTopologyRelease
            {
                Id = releaseId,
                TopologyId = 17,
                Version = 1,
                SourceRevision = 1,
                CanonicalJson = "{}",
                ContentHash = "lifecycle-release"
            });
            context.TeamLabRuntimes.Add(runtime);
            context.PenetrationGameLabBindings.Add(new PenetrationGameLabBinding
            {
                GameId = 7,
                TopologyId = 17,
                ActiveReleaseId = releaseId,
                MaxResetCount = maxResetCount
            });
            context.PenetrationTeamRuntimeBindings.Add(new PenetrationTeamRuntimeBinding
            {
                GameId = 7,
                TeamId = 11,
                RuntimeId = runtime.Id
            });
            await context.SaveChangesAsync();
        }
    }
}
