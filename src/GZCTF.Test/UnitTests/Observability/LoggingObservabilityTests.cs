using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Security.Claims;
using GZCTF.Extensions;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Admin;
using GZCTF.Repositories;
using GZCTF.Infrastructure.Observability;
using GZCTF.Utils;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
    public void OperationalLog_PreservesCallerDetailBudgetAlongsideIdentityMetadata()
    {
        using var context = CreateContext();
        var writer = new EfOperationalEventWriter(context,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EfOperationalEventWriter>.Instance);
        var reason = new string('r', 4080);
        var item = writer.Append(new OperationalEventDraft(OperationalEventCodes.Node.CapabilityChanged,
            OperationalEventOutcome.Observed, "observed", WorkerNodeName: new string('n', 128),
            Detail: new Dictionary<string, object?> { ["reason"] = reason }));
        Assert.InRange(item.DetailJson!.Length, 4097, 8192);
        using var json = System.Text.Json.JsonDocument.Parse(item.DetailJson);
        Assert.Equal(reason, json.RootElement.GetProperty("reason").GetString());
        Assert.Throws<ArgumentException>(() => writer.Append(new OperationalEventDraft(
            OperationalEventCodes.Node.CapabilityChanged, OperationalEventOutcome.Observed, "observed",
            Detail: new Dictionary<string, object?> { ["reason"] = new string('r', 4096) })));
    }

    [Theory]
    [InlineData("User")]
    [InlineData("System")]
    public void OperationalLog_OverwritesUntrustedIdentityDetail(string kind)
    {
        using var context = CreateContext();
        var actorId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
            new Claim(ClaimTypes.Name, "admin password=redacted-fixture")
        }, "test"));
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.18");
        var writer = new EfOperationalEventWriter(context,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EfOperationalEventWriter>.Instance,
            new HttpContextAccessor { HttpContext = http });
        var item = writer.Append(new OperationalEventDraft(OperationalEventCodes.Node.CapabilityChanged,
            OperationalEventOutcome.Observed, "observed", ActorKind: kind, Detail: new Dictionary<string, object?>
            {
                ["ActorUserId"] = "untrusted", ["actorKind"] = "untrusted",
                ["ActorDisplayName"] = "untrusted", ["RemoteIp"] = "untrusted",
                ["WorkerNodeName"] = "untrusted"
            }));
        using var detail = System.Text.Json.JsonDocument.Parse(item.DetailJson!);
        Assert.DoesNotContain("untrusted", item.DetailJson);
        Assert.Equal(kind, detail.RootElement.GetProperty("actorKind").GetString());
        if (kind == "System")
        {
            Assert.Null(item.ActorUserId);
            Assert.False(detail.RootElement.TryGetProperty("remoteIp", out _));
        }
        else
        {
            Assert.Equal(actorId, item.ActorUserId);
            Assert.Equal(actorId, detail.RootElement.GetProperty("actorUserId").GetGuid());
            Assert.DoesNotContain("redacted-fixture", item.DetailJson);
            Assert.Equal("192.0.2.18", detail.RootElement.GetProperty("remoteIp").GetString());
        }
    }

    [Fact]
    public void AgentLog_PreservesConnectionIpWithoutBorrowingRequestUserOrResourceOwner()
    {
        using var context = CreateContext();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Name, "admin")
        }, "test"));
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.18");
        var ownerId = Guid.NewGuid();
        var writer = new EfOperationalEventWriter(context,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EfOperationalEventWriter>.Instance,
            new HttpContextAccessor { HttpContext = http });
        var item = writer.Append(new OperationalEventDraft(OperationalEventCodes.Node.CapabilityChanged,
            OperationalEventOutcome.Observed, "observed", ActorKind: "Agent", OwnerUserId: ownerId));
        Assert.Null(item.ActorUserId);
        Assert.Equal(ownerId, item.OwnerUserId);
        using var detail = System.Text.Json.JsonDocument.Parse(item.DetailJson!);
        Assert.Equal("Agent", detail.RootElement.GetProperty("actorKind").GetString());
        Assert.Equal("192.0.2.18", detail.RootElement.GetProperty("remoteIp").GetString());
        Assert.False(detail.RootElement.TryGetProperty("actorDisplayName", out _));
    }

    [Fact]
    public void LegacyUserLog_DoesNotUseCachedLastLoginIp()
    {
        var capture = new CaptureSink();
        using var serilog = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(capture).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog));
        var logger = factory.CreateLogger<LoggingObservabilityTests>();
        logger.Log("explicit user event", new UserInfo { Id = Guid.NewGuid(), UserName = "user",
            IP = IPAddress.Parse("192.0.2.99") }, GZCTF.Utils.TaskStatus.Success);
        Assert.Null(LogModelFactory.FromLogEvent(capture.Event!).RemoteIP);
    }

    [Fact]
    public void SystemLogWithoutResult_DoesNotInventSuccessOrInheritRequestIdentity()
    {
        var capture = new CaptureSink();
        using var serilog = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(capture).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog));
        var logger = factory.CreateLogger<LoggingObservabilityTests>();
        using var request = logger.BeginScope(new Dictionary<string, object?>
        {
            ["ActorUserId"] = Guid.NewGuid(), ["ActorKind"] = "User", ["UserName"] = "admin",
            ["IP"] = IPAddress.Parse("192.0.2.18")
        });
        logger.SystemLog("system observation");
        var model = LogModelFactory.FromLogEvent(capture.Event!);
        Assert.Null(model.Status);
        Assert.Null(model.ActorUserId);
        Assert.Null(model.RemoteIP);
        Assert.Equal("System", model.ActorKind);
    }

    [Theory]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Information")]
    public void RawLogWithoutResult_DoesNotInventSuccess(string level)
    {
        var capture = new CaptureSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(capture).CreateLogger();
        logger.Write(Enum.Parse<LogEventLevel>(level), "no result recorded");
        var model = LogModelFactory.FromLogEvent(capture.Event!);
        Assert.Null(model.Status);
        Assert.Equal("Unknown", LogMessageModel.FromLogModel(model).ActorKind);
    }

    [Theory]
    [InlineData(OperationalEventOutcome.Observed, null)]
    [InlineData(OperationalEventOutcome.Recovered, null)]
    [InlineData(OperationalEventOutcome.Cancelled, null)]
    [InlineData(OperationalEventOutcome.Pending, -1)]
    [InlineData(OperationalEventOutcome.Blocked, -2)]
    [InlineData(OperationalEventOutcome.Succeeded, 0)]
    [InlineData(OperationalEventOutcome.Failed, 1)]
    public async Task OperationalLog_PreservesRequestIdentitySnapshotsAndExactOutcome(
        OperationalEventOutcome outcome, int? expectedStatus)
    {
        await using var context = CreateContext();
        var capture = new CaptureSink();
        using var serilog = new LoggerConfiguration().Destructure.AsScalar<IPAddress>()
            .Destructure.AsScalar<GZCTF.Utils.TaskStatus>().Enrich.FromLogContext().WriteTo.Sink(capture).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog));
        var actorId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()), new Claim(ClaimTypes.Name, "System")
        }, "test"));
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.18");
        var writer = new EfOperationalEventWriter(context, factory.CreateLogger<EfOperationalEventWriter>(),
            new HttpContextAccessor { HttpContext = http });
        var nodeId = Guid.NewGuid();
        var entity = writer.Append(new OperationalEventDraft(OperationalEventCodes.Node.CapabilityChanged,
            outcome, "Node capability changed.",
            ErrorCategory: outcome == OperationalEventOutcome.Failed ? OperationalErrorCategory.Unknown : null,
            ErrorCode: outcome == OperationalEventOutcome.Failed ? "node.failed" : null,
            WorkerNodeId: nodeId, ResourceType: "worker-node", ResourceId: nodeId.ToString(),
            ResourceDisplayName: "node-before-rename", WorkerNodeName: "node-before-rename"));
        var raw = LogModelFactory.FromLogEvent(capture.Event!);
        context.Logs.Add(raw);
        await context.SaveChangesAsync();
        var item = Assert.Single((await new LogRepository(context, new EfOperationalLogDisplayQuery(context))
            .GetLogs(new LogQueryModel(), CancellationToken.None)).Items);
        Assert.Equal("User", item.ActorKind); // A user named System remains a user.
        Assert.Equal(actorId, item.ActorUserId);
        Assert.Equal("System", item.ActorDisplayName);
        Assert.Equal(http.Connection.RemoteIpAddress, item.IP);
        Assert.Equal(outcome.ToString(), item.Outcome);
        Assert.Equal(expectedStatus, item.Status is { } status ? (int)status : null);
        Assert.Equal("node-before-rename", item.WorkerNodeName);
        Assert.Equal("node-before-rename", item.ResourceDisplayName);
        Assert.Equal("User", item.Detail!["actorKind"].GetString());
        Assert.Equal(actorId, entity.ActorUserId);
    }

    [Fact]
    public async Task HistoricalLog_ResolvesAvailableNamesWithoutInventingActorIpOrOutcome()
    {
        await using var context = CreateContext();
        var nodeId = Guid.NewGuid();
        context.WorkerNodes.Add(new WorkerNode { Id = nodeId, Name = "node-current" });
        context.Logs.Add(new LogModel
        {
            TimeUtc = DateTimeOffset.UtcNow, Level = "Information", Logger = "old-writer",
            UserName = "Anonymous", Message = "old event", Status = GZCTF.Utils.TaskStatus.Success,
            WorkerNodeId = nodeId, ResourceType = "worker-node", ResourceId = nodeId.ToString(),
            CorrelationId = Guid.NewGuid(), EventCode = OperationalEventCodes.Node.CapabilityChanged
        });
        await context.SaveChangesAsync();
        var item = Assert.Single((await new LogRepository(context, new EfOperationalLogDisplayQuery(context))
            .GetLogs(new LogQueryModel(), CancellationToken.None)).Items);
        Assert.Equal("node-current", item.WorkerNodeName);
        Assert.Equal("node-current", item.ResourceDisplayName);
        Assert.Equal(new[] { "node", "resource" }, item.CurrentDisplayNames);
        Assert.Equal("Unknown", item.ActorKind);
        Assert.Null(item.ActorUserId);
        Assert.Null(item.ActorDisplayName);
        Assert.Null(item.IP);
        Assert.Null(item.Outcome);
    }

    [Fact]
    public async Task LogDisplay_EnrichesActorWithoutChangingRawNameOrRecordedSnapshot()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        context.Users.Add(new UserInfo { Id = userId, UserName = "renamed password=redacted-fixture" });
        context.Logs.AddRange(new LogModel
        {
            TimeUtc = DateTimeOffset.UtcNow, Level = "Information", Logger = "test", Message = "missing-label",
            ActorKind = "User", ActorUserId = userId
        }, new LogModel
        {
            TimeUtc = DateTimeOffset.UtcNow, Level = "Information", Logger = "test", Message = "snapshot-label",
            ActorKind = "User", ActorUserId = userId, UserName = "name-before-rename"
        });
        await context.SaveChangesAsync();
        var items = (await new LogRepository(context, new EfOperationalLogDisplayQuery(context))
            .GetLogs(new LogQueryModel(), CancellationToken.None)).Items;
        var enriched = Assert.Single(items, item => item.Msg == "missing-label");
        Assert.Null(enriched.UserName);
        Assert.Equal("renamed password=[REDACTED]", enriched.ActorDisplayName);
        Assert.Equal(new[] { "actor" }, enriched.CurrentDisplayNames);
        var snapshot = Assert.Single(items, item => item.Msg == "snapshot-label");
        Assert.Equal("name-before-rename", snapshot.ActorDisplayName);
        Assert.Null(snapshot.CurrentDisplayNames);
    }

    [Fact]
    public async Task LogDisplay_UsesExplicitResourceTypeAndKeepsUnknownOrDeletedResourcesUnlabelled()
    {
        await using var context = CreateContext();
        var targetId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid();
        context.Users.Add(new UserInfo { Id = targetId, UserName = "target-user" });
        context.Games.Add(new Game { Id = 1, Title = "match" });
        context.Teams.Add(new Team { Id = 1, Name = "team" });
        context.TrainingCourses.Add(new TrainingCourse { Id = 1, Title = "course" });
        context.GameChallenges.Add(new GameChallenge { Id = 1, Title = "game-question" });
        context.ExerciseChallenges.Add(new ExerciseChallenge { Id = 1, Title = "exercise-question" });
        context.TeamLabRuntimes.Add(new TeamLabRuntime { PublicId = runtimeId, ExternalReference = "lab" });
        var expected = new Dictionary<string, (string Id, string? Label)>
        {
            ["user"] = (targetId.ToString(), "target-user"), ["game"] = ("1", "match"),
            ["team"] = ("1", "team"), ["training-course"] = ("1", "course"),
            ["game-challenge"] = ("1", "game-question"), ["exercise-challenge"] = ("1", "exercise-question"),
            ["teamlab-runtime"] = (runtimeId.ToString(), "lab"), ["challenge"] = ("1", null),
            ["vm"] = (Guid.NewGuid().ToString(), null)
        };
        foreach (var (type, value) in expected)
            context.Logs.Add(new LogModel { TimeUtc = DateTimeOffset.UtcNow, Level = "Information", Logger = "test",
                Message = type, ResourceType = type, ResourceId = value.Id });
        await context.SaveChangesAsync();
        var items = (await new LogRepository(context, new EfOperationalLogDisplayQuery(context))
            .GetLogs(new LogQueryModel(), CancellationToken.None)).Items;
        foreach (var item in items)
        {
            Assert.Equal(expected[item.ResourceType!].Label, item.ResourceDisplayName);
            Assert.Equal(item.ResourceDisplayName is null ? null : new[] { "resource" }, item.CurrentDisplayNames);
            Assert.Null(item.ActorUserId);
        }
    }

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
    public async Task DatabaseSink_DisposeDrainsAcceptedLogsAndCountsRejectedEmits()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();
        var sink = new DatabaseSink(provider);
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var droppedBefore = DatabaseLogSinkMetrics.Dropped;

        for (var index = 0; index < 10; index++)
            logger.Information("accepted log {Index}", index);
        sink.Dispose();
        logger.Information("rejected after dispose");

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(10, await context.Logs.CountAsync(item => item.Message.Contains("accepted log")));
        Assert.True(DatabaseLogSinkMetrics.Dropped >= droppedBefore + 1);
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
        var repository = new LogRepository(context, new EfOperationalLogDisplayQuery(context));

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
