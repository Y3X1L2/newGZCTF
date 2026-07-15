using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.Observability;

public sealed class OperationalEventContractTests
{
    [Fact]
    public void EventCodes_AreUniqueAndStable()
    {
        Assert.NotEmpty(OperationalEventCodes.All);
        Assert.Equal(OperationalEventCodes.All.Count,
            OperationalEventCodes.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(OperationalEventCodes.All, code =>
        {
            Assert.Equal(code.ToLowerInvariant(), code);
            Assert.DoesNotContain(' ', code);
            Assert.Contains('.', code);
        });
    }

    [Fact]
    public async Task AppendAndSave_PersistsCorrelationTraceAndAllowedDetail()
    {
        await using var context = CreateContext();
        var writer = new EfOperationalEventWriter(
            context, NullLogger<EfOperationalEventWriter>.Instance);
        var correlationId = Guid.CreateVersion7();
        using var source = new ActivitySource("GZCTF.Test.OperationalEvent");
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("event.test");

        var entity = await writer.AppendAndSaveAsync(new OperationalEventDraft(
            OperationalEventCodes.Runtime.TicketEnqueued,
            OperationalEventOutcome.Pending,
            "Runtime ticket queued.",
            CorrelationId: correlationId,
            Detail: new Dictionary<string, object?>
            {
                ["attempt"] = 1,
                ["workload"] = "VirtualMachine"
            },
            DeploymentTicketId: correlationId), CancellationToken.None);

        var persisted = await context.OperationalEvents.SingleAsync();
        Assert.Equal(entity.Id, persisted.Id);
        Assert.Equal(correlationId, persisted.CorrelationId);
        Assert.Equal(activity!.TraceId.ToString(), persisted.TraceId);
        Assert.Contains("\"attempt\":1", persisted.DetailJson);
        Assert.Equal(correlationId, persisted.DeploymentTicketId);
    }

    [Fact]
    public void Append_DoesNotCommitOutsideTheCallersUnitOfWork()
    {
        using var context = CreateContext();
        var writer = new EfOperationalEventWriter(
            context, NullLogger<EfOperationalEventWriter>.Instance);

        writer.Append(new OperationalEventDraft(
            OperationalEventCodes.Node.Online,
            OperationalEventOutcome.Observed,
            "Node became online."));

        Assert.Equal(EntityState.Added, context.Entry(context.OperationalEvents.Local.Single()).State);
    }

    [Fact]
    public void FailedEvent_RequiresTypedError()
    {
        using var context = CreateContext();
        var writer = new EfOperationalEventWriter(
            context, NullLogger<EfOperationalEventWriter>.Instance);

        Assert.Throws<ArgumentException>(() => writer.Append(new OperationalEventDraft(
            OperationalEventCodes.Runtime.ExecutionFailed,
            OperationalEventOutcome.Failed,
            "Runtime execution failed.")));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("registryAuth")]
    [InlineData("responseBody")]
    [InlineData("unregisteredKey")]
    public void Detail_RejectsSensitiveOrUnregisteredKeys(string key)
    {
        using var context = CreateContext();
        var writer = new EfOperationalEventWriter(
            context, NullLogger<EfOperationalEventWriter>.Instance);

        Assert.Throws<ArgumentException>(() => writer.Append(new OperationalEventDraft(
            OperationalEventCodes.Runtime.TicketEnqueued,
            OperationalEventOutcome.Pending,
            "Runtime ticket queued.",
            Detail: new Dictionary<string, object?> { [key] = "value" })));
    }

    [Fact]
    public void Append_RedactsSensitiveAssignmentsFromMessageAndDetailValues()
    {
        using var context = CreateContext();
        var writer = new EfOperationalEventWriter(
            context, NullLogger<EfOperationalEventWriter>.Instance);

        var entity = writer.Append(new OperationalEventDraft(
            OperationalEventCodes.Runtime.TicketEnqueued,
            OperationalEventOutcome.Pending,
            "Request failed with token=top-secret and Authorization: Bearer abc.def.",
            Detail: new Dictionary<string, object?>
            {
                ["reasonCode"] = "password: qwer1234!"
            }));

        Assert.DoesNotContain("top-secret", entity.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def", entity.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", entity.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("qwer1234", entity.DetailJson, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", entity.DetailJson, StringComparison.Ordinal);
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
