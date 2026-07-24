using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Shared;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public class PlayerRuntimeStatusProjectionTests
{
    [Fact]
    public async Task LatestSubjectStatus_ReturnsLatestTicketWithinSubject()
    {
        await using var context = CreateContext();
        var subject = DeploymentQueueRequest.GameContainer(23, 7, 19);
        var oldFailure = DeploymentQueueTicket.Create(subject);
        oldFailure.Status = DeploymentQueueTicketStatus.Failed;
        oldFailure.Stage = DeploymentStage.ImagePreparing;
        oldFailure.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        var stop = DeploymentQueueTicket.Create(subject with { Operation = RuntimeOperationKind.Stop });
        stop.Status = DeploymentQueueTicketStatus.Succeeded;
        stop.Stage = DeploymentStage.Ready;
        stop.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        context.DeploymentQueueTickets.AddRange(oldFailure, stop);
        await context.SaveChangesAsync();
        var queue = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);

        var status = await queue.GetLatestSubjectStatusAsync(subject, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(stop.Id, status.TicketId);
        Assert.Equal(RuntimeOperationKind.Stop, status.Operation);
    }

    [Fact]
    public void ActiveCreate_ProjectsPendingState()
    {
        var context = new ClientFlagContext();

        PlayerRuntimeStatusProjection.Apply(context, Status(
            DeploymentQueueTicketStatus.Running,
            DeploymentStage.ContainerCreating));

        Assert.Equal(ContainerEntryStatus.Pending, context.InstanceEntryStatus);
        Assert.Null(context.InstanceEntryError);
    }

    [Fact]
    public void FailedImagePreparation_ProjectsPlayerSafeReason()
    {
        var context = new ClientFlagContext();

        PlayerRuntimeStatusProjection.Apply(context, Status(
            DeploymentQueueTicketStatus.Failed,
            DeploymentStage.ImagePreparing,
            errorMessage: "Docker image 10.24.0.28:5000/private/demo is not registered."));

        Assert.Equal(ContainerEntryStatus.Error, context.InstanceEntryStatus);
        Assert.Equal("题目镜像暂不可用，请联系管理员。", context.InstanceEntryError);
        Assert.DoesNotContain("10.24.0.28", context.InstanceEntryError);
    }

    [Fact]
    public void ExistingContainerState_IsNotOverwrittenByHistoricalTicket()
    {
        var context = new ClientFlagContext
        {
            InstanceEntry = "10.24.0.30:32768",
            InstanceEntryStatus = ContainerEntryStatus.Ready
        };

        PlayerRuntimeStatusProjection.Apply(context, Status(
            DeploymentQueueTicketStatus.Failed,
            DeploymentStage.ContainerCreating));

        Assert.Equal(ContainerEntryStatus.Ready, context.InstanceEntryStatus);
        Assert.Equal("10.24.0.30:32768", context.InstanceEntry);
        Assert.Null(context.InstanceEntryError);
    }

    [Fact]
    public void CompletedStop_DoesNotResurfaceAnOlderCreateFailure()
    {
        var context = new ClientFlagContext();

        PlayerRuntimeStatusProjection.Apply(context, Status(
            DeploymentQueueTicketStatus.Succeeded,
            DeploymentStage.Ready,
            RuntimeOperationKind.Stop));

        Assert.Null(context.InstanceEntryStatus);
        Assert.Null(context.InstanceEntryError);
    }

    static DeploymentQueueStatusModel Status(
        DeploymentQueueTicketStatus status,
        DeploymentStage stage,
        RuntimeOperationKind operation = RuntimeOperationKind.Create,
        string? errorMessage = null) =>
        new(
            Guid.CreateVersion7(),
            DeploymentQueueKind.GameContainer,
            status,
            operation,
            stage,
            null,
            null,
            0,
            0,
            errorMessage,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow);

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
