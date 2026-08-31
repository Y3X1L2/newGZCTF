using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabWebhookTests
{
    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://10.0.0.1/hook")]
    [InlineData("https://172.16.3.9/hook")]
    [InlineData("https://192.168.1.20/hook")]
    [InlineData("https://169.254.1.1/hook")]
    [InlineData("https://224.0.0.5/hook")]
    [InlineData("https://fe80::1/hook")]
    [InlineData("https://0.0.0.0/hook")]
    [InlineData("not-a-url")]
    public void EndpointValidator_RejectsNonPublicOrNonHttpsEndpoints(string endpoint)
    {
        var exception = Assert.ThrowsAsync<TeamLabApiContractException>(async () =>
            await TeamLabWebhookEndpointValidator.ValidateAndNormalizeAsync(endpoint, default)).Result;
        Assert.Equal(TeamLabWebhookErrorCodes.EndpointInvalid, exception.Code);
    }

    [Fact]
    public void EndpointValidator_AcceptsPublicLiteralAddress()
    {
        var normalized = TeamLabWebhookEndpointValidator
            .ValidateAndNormalizeAsync("https://8.8.8.8/hook", default).Result;
        Assert.StartsWith("https://8.8.8.8/", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ForbiddenAddressClassification_CoversPrivateAndReservedRanges()
    {
        Assert.True(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("10.1.2.3")));
        Assert.True(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("172.20.0.1")));
        Assert.True(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("192.168.9.9")));
        Assert.True(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("100.64.0.1")));
        Assert.True(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("198.18.0.1")));
        Assert.True(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("::1")));
        Assert.True(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("fc00::1")));
        Assert.True(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("fd12:3456::1")));
        Assert.False(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("8.8.8.8")));
        Assert.False(TeamLabWebhookEndpointValidator.IsForbidden(
            System.Net.IPAddress.Parse("203.0.113.10")));
    }

    [Fact]
    public void Signature_IsStableHmacSha256Hex()
    {
        var first = TeamLabWebhookDelivery.ComputeSignature("secret", "body");
        var second = TeamLabWebhookDelivery.ComputeSignature("secret", "body");
        Assert.Equal(first, second);
        Assert.StartsWith("sha256=", first, StringComparison.Ordinal);
        Assert.NotEqual(first, TeamLabWebhookDelivery.ComputeSignature("other", "body"));
    }

    [Fact]
    public void Envelope_IsStableForTheSameEvent()
    {
        var runtime = new TeamLabRuntime { PublicId = Guid.NewGuid(), Generation = 3 };
        var localEvent = new TeamLabEvent
        {
            Id = 42,
            RuntimeId = 1,
            Generation = 3,
            Stage = "runtime-probing",
            Level = TeamLabEventLevel.Info,
            Message = "探测完成",
            CreatedAt = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero),
            ObjectType = "asset",
            ObjectId = "web-1"
        };
        var scopeId = Guid.NewGuid();

        var envelope = TeamLabWebhookDelivery.BuildEnvelope(localEvent, runtime, scopeId);
        var envelopeAgain = TeamLabWebhookDelivery.BuildEnvelope(localEvent, runtime, scopeId);

        Assert.Equal(envelope, envelopeAgain);
        Assert.Equal("teamlab:42", envelope.Id);
        Assert.Equal(scopeId, envelope.ScopeId);
        Assert.Equal(runtime.PublicId, envelope.ResourceId);
        Assert.Equal(3, envelope.ResourceVersion);
        Assert.Equal("web-1", envelope.AssetKey);
        Assert.Contains("/api/open/v1/teamlab/runtimes/", envelope.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Envelope_NoAssetKeyWhenObjectIsNotAsset()
    {
        var runtime = new TeamLabRuntime { PublicId = Guid.NewGuid(), Generation = 1 };
        var localEvent = new TeamLabEvent
        {
            Id = 7,
            RuntimeId = 1,
            Generation = 1,
            Stage = "deploy",
            Message = "创建完成",
            ObjectType = "shard",
            ObjectId = "9"
        };
        var envelope = TeamLabWebhookDelivery.BuildEnvelope(localEvent, runtime, Guid.NewGuid());
        Assert.Null(envelope.AssetKey);
    }

    [Fact]
    public void RetryDelay_BoundedExponentialBackoff()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), TeamLabWebhookDelivery.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(10), TeamLabWebhookDelivery.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(300), TeamLabWebhookDelivery.RetryDelay(20));
        Assert.Equal(TimeSpan.FromSeconds(300), TeamLabWebhookDelivery.RetryDelay(int.MaxValue));
    }

    [Fact]
    public void EventTypes_RoundTripNormalizesAndOrders()
    {
        var json = TeamLabWebhookDelivery.SerializeEventTypes(["capture-failed", "deploy", "capture-failed"]);
        var parsed = TeamLabWebhookDelivery.ParseEventTypes(json);
        Assert.Equal(["capture-failed", "deploy"], parsed);
        Assert.Empty(TeamLabWebhookDelivery.ParseEventTypes("[]"));
        Assert.Empty(TeamLabWebhookDelivery.ParseEventTypes(null));
    }

    [Fact]
    public void Service_AdvancesCursorOnSuccessAndRetriesOnFailure()
    {
        using var context = CreateContext();
        var protection = DataProtectionProvider.Create("GZCTF.Test.Webhook");
        var scope = new TeamLabControlScope { Id = Guid.NewGuid(), Key = "scope-w", DisplayName = "scope" };
        context.TeamLabControlScopes.Add(scope);
        var runtime = new TeamLabRuntime
        {
            Id = 1,
            PublicId = Guid.NewGuid(),
            ControlScopeId = scope.Id,
            Generation = 1,
            TopologyReleaseId = Guid.NewGuid(),
            Status = TeamLabRuntimeStatus.Running,
            IsOpenToPlayers = false,
            CreatedById = Guid.NewGuid()
        };
        context.TeamLabRuntimes.Add(runtime);
        context.TeamLabEvents.AddRange(
            Event(1, scope.Id, runtime.PublicId, "deploy", "a"),
            Event(2, scope.Id, runtime.PublicId, "ready", "b"),
            Event(3, scope.Id, runtime.PublicId, "ready", "c"));
        context.SaveChanges();

        var deliverer = new StubDeliverer(failEventIds: [2]);
        var service = new TeamLabWebhookService(context, protection, deliverer);
        var created = service.CreateForOperationAsync(new CreateTeamLabWebhookModel(scope.Id, "https://8.8.8.8/hook", ["ready"], Enabled: true, FromEventId: 1), Guid.NewGuid(), Guid.NewGuid(), default).Result;

        // First pass: event 1 does not match the filter (cursor advances silently),
        // event 2 matches but fails -> retry scheduled.
        var delivered = service.DeliverPendingAsync(default).Result;
        Assert.Equal(0, delivered);
        var afterFirst = context.TeamLabWebhookSubscriptions.Single(item => item.PublicId == created.Id);
        Assert.Equal(1, afterFirst.DeliveryCursor);
        Assert.Equal(1, afterFirst.ConsecutiveFailures);
        Assert.NotNull(afterFirst.NextDeliveryAt);
        Assert.Equal(1, afterFirst.Failures.Count);
        Assert.Equal(2, afterFirst.Failures[0].EventId);

        // Retry pass (backoff window elapsed): event 2 delivered, then event 3 follows.
        var retried = context.TeamLabWebhookSubscriptions.Single(item => item.PublicId == created.Id);
        retried.NextDeliveryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        context.SaveChanges();
        deliverer.FailEventIds.Clear();
        var replayed = service.DeliverPendingAsync(default).Result;
        Assert.Equal(2, replayed);
        var afterSecond = context.TeamLabWebhookSubscriptions.Single(item => item.PublicId == created.Id);
        Assert.Equal(3, afterSecond.DeliveryCursor);
        Assert.Equal(0, afterSecond.ConsecutiveFailures);
        Assert.Null(afterSecond.NextDeliveryAt);
    }

    [Fact]
    public void Service_ReplayDoesNotAdvanceCursor()
    {
        using var context = CreateContext();
        var protection = DataProtectionProvider.Create("GZCTF.Test.Webhook");
        var scope = new TeamLabControlScope { Id = Guid.NewGuid(), Key = "scope-r", DisplayName = "scope" };
        context.TeamLabControlScopes.Add(scope);
        var runtime = new TeamLabRuntime
        {
            Id = 1,
            PublicId = Guid.NewGuid(),
            ControlScopeId = scope.Id,
            Generation = 1,
            TopologyReleaseId = Guid.NewGuid(),
            Status = TeamLabRuntimeStatus.Running,
            IsOpenToPlayers = false,
            CreatedById = Guid.NewGuid()
        };
        context.TeamLabRuntimes.Add(runtime);
        context.TeamLabEvents.Add(Event(10, scope.Id, runtime.PublicId, "ready", "x"));
        context.SaveChanges();

        var deliverer = new StubDeliverer(Array.Empty<long>());
        var service = new TeamLabWebhookService(context, protection, deliverer);
        var created = service.CreateForOperationAsync(new CreateTeamLabWebhookModel(scope.Id, "https://8.8.8.8/hook", [], Enabled: true, FromEventId: 1), Guid.NewGuid(), Guid.NewGuid(), default).Result;

        service.DeliverPendingAsync(default).Wait();
        var cursorAfterDelivery = context.TeamLabWebhookSubscriptions.Single(item => item.PublicId == created.Id).DeliveryCursor;
        Assert.Equal(10, cursorAfterDelivery);

        var result = service.ReplayAsync(created.Id, 1, default).Result;
        Assert.Equal(1, result.Delivered);
        Assert.Equal(10, context.TeamLabWebhookSubscriptions.Single(item => item.PublicId == created.Id).DeliveryCursor);
    }

    [Fact]
    public void Service_FromEventIdIsInclusiveAndReplayRespectsFilter()
    {
        using var context = CreateContext();
        var protection = DataProtectionProvider.Create("GZCTF.Test.Webhook.Inclusive");
        var scope = new TeamLabControlScope { Id = Guid.NewGuid(), Key = "scope-i", DisplayName = "scope" };
        context.TeamLabControlScopes.Add(scope);
        var runtimeId = Guid.NewGuid();
        context.TeamLabEvents.AddRange(
            Event(1, scope.Id, runtimeId, "ready", "included"),
            Event(2, scope.Id, runtimeId, "deploy", "filtered"));
        context.SaveChanges();
        var deliverer = new StubDeliverer([]);
        var service = new TeamLabWebhookService(context, protection, deliverer);
        var created = service.CreateForOperationAsync(
            new CreateTeamLabWebhookModel(scope.Id, "https://8.8.8.8/hook", ["ready"], true, 1),
            Guid.NewGuid(), Guid.NewGuid(), default).Result;

        Assert.Equal(1, service.DeliverPendingAsync(default).Result);
        var replay = service.ReplayAsync(created.Id, 1, default).Result;

        Assert.Equal(1, replay.Delivered);
        Assert.Equal(0, replay.Failed);
    }

    [Fact]
    public void Service_DisablesSubscriptionAtFailureThreshold()
    {
        using var context = CreateContext();
        var protection = DataProtectionProvider.Create("GZCTF.Test.Webhook.Disable");
        var scope = new TeamLabControlScope { Id = Guid.NewGuid(), Key = "scope-d", DisplayName = "scope" };
        context.TeamLabControlScopes.Add(scope);
        context.TeamLabEvents.Add(Event(1, scope.Id, Guid.NewGuid(), "ready", "fails"));
        context.SaveChanges();
        var deliverer = new StubDeliverer([1]);
        var service = new TeamLabWebhookService(context, protection, deliverer);
        var created = service.CreateForOperationAsync(
            new CreateTeamLabWebhookModel(scope.Id, "https://8.8.8.8/hook", [], true, 1),
            Guid.NewGuid(), Guid.NewGuid(), default).Result;

        for (var attempt = 0; attempt < TeamLabWebhookDelivery.MaxConsecutiveFailures; attempt++)
        {
            service.DeliverPendingAsync(default).Wait();
            var tracked = context.TeamLabWebhookSubscriptions.Single(item => item.PublicId == created.Id);
            if (tracked.Active)
            {
                tracked.NextDeliveryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
                context.SaveChanges();
            }
        }

        var disabled = context.TeamLabWebhookSubscriptions.Single(item => item.PublicId == created.Id);
        Assert.False(disabled.Active);
        Assert.Equal(TeamLabWebhookDelivery.MaxConsecutiveFailures, disabled.ConsecutiveFailures);
        Assert.Null(disabled.NextDeliveryAt);
    }

    [Fact]
    public void Service_RevokeStopsFurtherDelivery()
    {
        using var context = CreateContext();
        var protection = DataProtectionProvider.Create("GZCTF.Test.Webhook");
        var scope = new TeamLabControlScope { Id = Guid.NewGuid(), Key = "scope-v", DisplayName = "scope" };
        context.TeamLabControlScopes.Add(scope);
        var service = new TeamLabWebhookService(context, protection, new StubDeliverer(Array.Empty<long>()));
        var created = service.CreateForOperationAsync(new CreateTeamLabWebhookModel(scope.Id, "https://8.8.8.8/hook", [], Enabled: true, FromEventId: 1), Guid.NewGuid(), Guid.NewGuid(), default).Result;

        service.RevokeAsync(created.Id, default).Wait();
        var model = service.GetAsync(created.Id, default).Result;
        Assert.False(model.Active);
        Assert.NotNull(model.RevokedAt);
    }

    [Fact]
    public void OperationResult_ReturnsSigningSecretOnlyOnce()
    {
        using var context = CreateContext();
        var protection = DataProtectionProvider.Create("GZCTF.Test.Webhook.Secret");
        var scope = new TeamLabControlScope { Id = Guid.NewGuid(), Key = "scope-s", DisplayName = "scope" };
        context.TeamLabControlScopes.Add(scope);
        var operationId = Guid.NewGuid();
        var service = new TeamLabWebhookService(context, protection, new StubDeliverer([]));
        var webhook = service.CreateForOperationAsync(
            new CreateTeamLabWebhookModel(scope.Id, "https://8.8.8.8/hook", [], true, 1),
            Guid.NewGuid(), operationId, default).Result;
        context.TeamLabRuntimeOperationJobs.Add(new TeamLabRuntimeOperationJob
        {
            OperationId = operationId,
            Kind = TeamLabRuntimeOperationKind.WebhookCreate,
            ResultJson = JsonSerializer.Serialize(webhook),
            CompletedAt = DateTimeOffset.UtcNow
        });
        context.SaveChanges();
        var provider = new TeamLabRuntimeOperationResultProvider(context, null!, protection);

        var first = provider.GetResultAsync(operationId, default).Result!.Value;
        var secret = first.GetProperty("SigningSecret").GetString();
        var second = provider.GetResultAsync(operationId, default).Result!.Value;

        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.Equal(webhook.Id, first.GetProperty("Webhook").GetProperty("Id").GetGuid());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("SigningSecret").ValueKind);
        Assert.DoesNotContain(secret!, context.TeamLabRuntimeOperationJobs.Single().ResultJson!, StringComparison.Ordinal);
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TeamLabEvent Event(long id, Guid scopeId, Guid runtimeId, string stage, string message) => new()
    {
        Id = checked((int)id),
        RuntimeId = 1,
        ControlScopeId = scopeId,
        Generation = 1,
        Stage = stage,
        Message = message,
        ResourceType = "teamlab-runtime",
        ResourcePublicId = runtimeId,
        ResourceVersion = 1,
        ResourceUrl = $"/api/open/v1/teamlab/runtimes/{runtimeId:D}"
    };

    private sealed class StubDeliverer : ITeamLabWebhookDeliverer
    {
        public StubDeliverer(IEnumerable<long> failEventIds) => FailEventIds = failEventIds.ToHashSet();
        public HashSet<long> FailEventIds { get; }

        public Task<TeamLabWebhookDeliveryResult> DeliverAsync(
            TeamLabWebhookSubscriptionView subscription,
            TeamLabWebhookEventEnvelope envelope,
            string body,
            string signature,
            CancellationToken cancellationToken)
        {
            var eventId = long.Parse(envelope.Id.Split(':')[^1], System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(FailEventIds.Contains(eventId)
                ? new TeamLabWebhookDeliveryResult(false, "stub_failure")
                : new TeamLabWebhookDeliveryResult(true, string.Empty));
        }
    }
}
