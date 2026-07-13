using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Extensions;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Admin;
using GZCTF.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace GZCTF.Test.UnitTests.Observability;

public sealed class LoggingObservabilityTests
{
    [Fact]
    public void LogModelFactory_MapsStructuredOperationalProperties()
    {
        var capture = new CaptureSink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(capture)
            .CreateLogger();
        var correlationId = Guid.CreateVersion7();
        var nodeId = Guid.CreateVersion7();

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TraceId", "0123456789abcdef"))
        using (LogContext.PushProperty("EventCode", "runtime.ticket.enqueued"))
        using (LogContext.PushProperty("ErrorCategory", "Capacity"))
        using (LogContext.PushProperty("ErrorCode", "runtime.capacity_exhausted"))
        using (LogContext.PushProperty("WorkerNodeId", nodeId))
        using (LogContext.PushProperty("WorkerNodeName", "worker-a"))
        using (LogContext.PushProperty("ResourceType", "vm"))
        using (LogContext.PushProperty("ResourceId", "vm-1"))
        {
            logger.Information("structured event");
        }

        var model = LogModelFactory.FromLogEvent(capture.Event!);
        Assert.Equal(correlationId, model.CorrelationId);
        Assert.Equal("runtime.ticket.enqueued", model.EventCode);
        Assert.Equal("runtime.capacity_exhausted", model.ErrorCode);
        Assert.Equal(nodeId, model.WorkerNodeId);
        Assert.Equal("worker-a", model.WorkerNodeName);
        Assert.Equal("vm", model.ResourceType);
        Assert.Equal("vm-1", model.ResourceId);
    }

    [Fact]
    public async Task DatabaseSink_FlushesSingleLowVolumeLogWithoutAnotherSignal()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();
        using var sink = new DatabaseSink(provider);
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        logger.Information("single low-volume log");

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await context.Logs.AnyAsync(item => item.Message.Contains("single low-volume log")))
                return;
            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("The database log sink did not flush within five seconds.");
    }

    [Fact]
    public async Task LogRepository_FiltersByCorrelationTicketAndResource()
    {
        await using var context = CreateContext();
        var expectedCorrelation = Guid.CreateVersion7();
        var expectedTicket = Guid.CreateVersion7();
        context.Logs.AddRange(
            Log(expectedCorrelation, expectedTicket, "vm", "vm-1"),
            Log(Guid.CreateVersion7(), Guid.CreateVersion7(), "container", "container-1"));
        await context.SaveChangesAsync();
        var repository = new LogRepository(context);

        var result = await repository.GetLogs(new LogQueryModel
        {
            CorrelationId = expectedCorrelation,
            DeploymentTicketId = expectedTicket,
            ResourceType = "vm",
            ResourceId = "vm-1"
        }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(expectedCorrelation, item.CorrelationId);
        Assert.Equal(expectedTicket, item.DeploymentTicketId);
        Assert.Equal("vm-1", item.ResourceId);
    }

    private static LogModel Log(Guid correlationId, Guid deploymentTicketId, string resourceType, string resourceId) => new()
    {
        TimeUtc = DateTimeOffset.UtcNow,
        Level = "Information",
        Logger = "test",
        Message = "test",
        CorrelationId = correlationId,
        DeploymentTicketId = deploymentTicketId,
        ResourceType = resourceType,
        ResourceId = resourceId
    };

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class CaptureSink : ILogEventSink
    {
        public LogEvent? Event { get; private set; }
        public void Emit(LogEvent logEvent) => Event = logEvent;
    }
}
