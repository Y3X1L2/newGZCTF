using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Controllers;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Observability;

public sealed class OperationalEventQueryTests
{
    [Fact]
    public async Task QueryAsync_UsesStableCursorAndResolvesLabels()
    {
        await using var context = CreateContext();
        var nodeId = Guid.CreateVersion7();
        context.WorkerNodes.Add(new WorkerNode
        {
            Id = nodeId,
            Name = "worker-alpha",
            HostAddress = "10.24.0.31",
            AuthToken = "test"
        });
        context.ImageTemplates.Add(new ImageTemplate { Id = 7, Name = "ubuntu-cloud" });
        var occurredAt = DateTimeOffset.Parse("2026-07-13T12:00:00Z");
        context.OperationalEvents.AddRange(
            Event(101, occurredAt, OperationalEventCodes.Image.DistributionQueued,
                workerNodeId: nodeId, imageTemplateId: 7),
            Event(102, occurredAt, OperationalEventCodes.Image.TransferStarted,
                workerNodeId: nodeId, imageTemplateId: 7),
            Event(103, occurredAt, OperationalEventCodes.Image.DistributionReady,
                workerNodeId: nodeId, imageTemplateId: 7));
        await context.SaveChangesAsync();
        var service = new OperationalEventQueryService(context);

        var first = await service.QueryAsync(new OperationalEventQueryModel { Count = 2 }, CancellationToken.None);
        var second = await service.QueryAsync(new OperationalEventQueryModel
        {
            Count = 2,
            Cursor = first.NextCursor
        }, CancellationToken.None);

        Assert.Equal([103L, 102L], first.Items.Select(item => item.Event.Id).ToArray());
        Assert.NotNull(first.NextCursor);
        Assert.Equal([101L], second.Items.Select(item => item.Event.Id).ToArray());
        Assert.Null(second.NextCursor);
        Assert.All(first.Items, item =>
        {
            Assert.Equal("worker-alpha", item.Labels.WorkerNode);
            Assert.Equal("ubuntu-cloud", item.Labels.ImageTemplate);
            Assert.Equal("image", item.Domain);
        });
    }

    [Fact]
    public async Task QueryAsync_AppliesTypedAndBusinessFilters()
    {
        await using var context = CreateContext();
        var nodeId = Guid.CreateVersion7();
        var otherNodeId = Guid.CreateVersion7();
        context.OperationalEvents.AddRange(
            Event(1, DateTimeOffset.UtcNow, OperationalEventCodes.Runtime.ExecutionFailed,
                outcome: OperationalEventOutcome.Failed,
                errorCategory: OperationalErrorCategory.NodeUnavailable,
                workerNodeId: nodeId,
                resourceType: "container",
                resourceId: "runtime-1"),
            Event(2, DateTimeOffset.UtcNow.AddSeconds(-1), OperationalEventCodes.Runtime.ExecutionSucceeded,
                workerNodeId: nodeId,
                resourceType: "container",
                resourceId: "runtime-1"),
            Event(3, DateTimeOffset.UtcNow.AddSeconds(-2), OperationalEventCodes.Runtime.ExecutionFailed,
                outcome: OperationalEventOutcome.Failed,
                errorCategory: OperationalErrorCategory.NodeUnavailable,
                workerNodeId: otherNodeId,
                resourceType: "container",
                resourceId: "runtime-1"));
        await context.SaveChangesAsync();

        var page = await new OperationalEventQueryService(context).QueryAsync(new OperationalEventQueryModel
        {
            Domain = "runtime",
            Outcome = OperationalEventOutcome.Failed,
            ErrorCategory = OperationalErrorCategory.NodeUnavailable,
            WorkerNodeId = nodeId,
            ResourceType = "container",
            ResourceId = "runtime-1"
        }, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(1, item.Event.Id);
    }

    [Fact]
    public async Task QueryRecoveryAsync_ReturnsRecoveryAndInventoryDriftOnly()
    {
        await using var context = CreateContext();
        context.OperationalEvents.AddRange(
            Event(1, DateTimeOffset.UtcNow, OperationalEventCodes.Recovery.ResourceMissing),
            Event(2, DateTimeOffset.UtcNow.AddSeconds(-1), OperationalEventCodes.Agent.InventoryUnavailable),
            Event(3, DateTimeOffset.UtcNow.AddSeconds(-2), OperationalEventCodes.Runtime.ExecutionFailed,
                outcome: OperationalEventOutcome.Failed,
                errorCategory: OperationalErrorCategory.Unknown));
        await context.SaveChangesAsync();

        var page = await new OperationalEventQueryService(context)
            .QueryRecoveryAsync(new OperationalEventQueryModel(), CancellationToken.None);

        Assert.Equal([1L, 2L], page.Items.Select(item => item.Event.Id).ToArray());
    }

    [Fact]
    public async Task GetCorrelationAsync_SummarizesFailureAndTimeline()
    {
        await using var context = CreateContext();
        var correlationId = Guid.CreateVersion7();
        context.OperationalEvents.AddRange(
            Event(1, DateTimeOffset.Parse("2026-07-13T10:00:00Z"), OperationalEventCodes.Runtime.ExecutionStarted,
                correlationId: correlationId,
                subjectDisplayName: "admin team"),
            Event(2, DateTimeOffset.Parse("2026-07-13T10:00:03Z"), OperationalEventCodes.Runtime.ExecutionFailed,
                correlationId: correlationId,
                outcome: OperationalEventOutcome.Failed,
                errorCategory: OperationalErrorCategory.Docker,
                resourceDisplayName: "nginx:latest"));
        await context.SaveChangesAsync();

        var summary = await new OperationalEventQueryService(context)
            .GetCorrelationAsync(correlationId, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(OperationalEventOutcome.Failed, summary.Outcome);
        Assert.Equal(OperationalErrorCategory.Docker, summary.ErrorCategory);
        Assert.Equal(2, summary.EventCount);
        Assert.Equal(["runtime"], summary.Domains);
        Assert.Equal("admin team", summary.Subject);
        Assert.Equal("nginx:latest", summary.Resource);
        Assert.Equal([2L, 1L], summary.Timeline.Items.Select(item => item.Event.Id).ToArray());
    }

    [Fact]
    public void ControllerRoutes_PreserveQueueContractAndExposeOperationsApi()
    {
        Assert.Equal("api/v1/deployment-queue",
            typeof(DeploymentQueueController).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal("api/admin/operations",
            typeof(OperationsController).GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal("events", HttpGetTemplate<OperationsController>(nameof(OperationsController.Events)));
        Assert.Equal("recovery", HttpGetTemplate<OperationsController>(nameof(OperationsController.Recovery)));
        Assert.Equal("correlations/{correlationId:guid}",
            HttpGetTemplate<OperationsController>(nameof(OperationsController.Correlation)));
    }

    private static string? HttpGetTemplate<TController>(string methodName) =>
        typeof(TController).GetMethod(methodName)?.GetCustomAttribute<HttpGetAttribute>()?.Template;

    private static OperationalEvent Event(
        long id,
        DateTimeOffset occurredAt,
        string eventCode,
        Guid? correlationId = null,
        OperationalEventOutcome outcome = OperationalEventOutcome.Observed,
        OperationalErrorCategory? errorCategory = null,
        Guid? workerNodeId = null,
        int? imageTemplateId = null,
        string? resourceType = null,
        string? resourceId = null,
        string? subjectDisplayName = null,
        string? resourceDisplayName = null) => new()
    {
        Id = id,
        OccurredAt = occurredAt,
        CorrelationId = correlationId ?? Guid.CreateVersion7(),
        EventCode = eventCode,
        Outcome = outcome,
        ErrorCategory = errorCategory,
        ErrorCode = errorCategory is null ? null : "test_error",
        Message = eventCode,
        WorkerNodeId = workerNodeId,
        ImageTemplateId = imageTemplateId,
        ResourceType = resourceType,
        ResourceId = resourceId,
        SubjectDisplayName = subjectDisplayName,
        ResourceDisplayName = resourceDisplayName
    };

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
