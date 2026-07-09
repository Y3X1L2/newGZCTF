using System;
using GZCTF.Controllers;
using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class GameControllerQueueResponseTests
{
    [Fact]
    public void BuildVmCreateFallback_ReturnsAcceptedQueueStatus_WhenVmCreationWasQueued()
    {
        var queueState = new DeploymentQueueStateAccessor();
        var status = new DeploymentQueueStatusModel(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DeploymentQueueKind.Vm,
            DeploymentQueueTicketStatus.Pending,
            null,
            null,
            QueuePosition: 3,
            PeopleAhead: 2,
            ErrorMessage: null,
            DateTimeOffset.Parse("2026-07-06T00:00:00Z"),
            StartedAt: null,
            CompletedAt: null);
        queueState.SetQueued(status);

        var result = GameController.BuildVmCreateFallback(queueState);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var text = accepted.Value!.ToString();
        Assert.Contains("queued", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Payload", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flag{", text, StringComparison.OrdinalIgnoreCase);
        Assert.Null(queueState.ConsumeQueued());
    }

    [Fact]
    public void BuildVmCreateFallback_ReturnsConcreteError_WhenVmCreationFailedAfterScheduling()
    {
        var queueState = new DeploymentQueueStateAccessor();

        var result = GameController.BuildVmCreateFallback(queueState,
            "Node worker-1 failed to ensure VM template windows from storage");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var text = badRequest.Value!.ToString();
        Assert.Contains("failed to ensure VM template", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No KVM node available", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildVmCreateAccepted_ReturnsAcceptedCreatingStatusWithQueue()
    {
        var status = new DeploymentQueueStatusModel(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DeploymentQueueKind.Vm,
            DeploymentQueueTicketStatus.Pending,
            null,
            null,
            QueuePosition: 1,
            PeopleAhead: 0,
            ErrorMessage: null,
            DateTimeOffset.Parse("2026-07-06T00:00:00Z"),
            StartedAt: null,
            CompletedAt: null);

        var result = GameController.BuildVmCreateAccepted(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            status);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var text = accepted.Value!.ToString();
        Assert.Contains("Creating", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("image-pending", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("queue", text, StringComparison.OrdinalIgnoreCase);
    }
}
