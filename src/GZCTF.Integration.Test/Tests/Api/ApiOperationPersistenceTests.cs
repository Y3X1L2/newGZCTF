using System.Net;
using System.Net.Http.Headers;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ApiOperationPersistenceTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task ExpiredLease_RejectsOwnerWritesBeforeAnotherWorkerClaims()
    {
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var tokenId = Guid.CreateVersion7();
        var operations = Enumerable.Range(0, 4)
            .Select(index => new ApiOperation
            {
                Kind = "test.lease",
                Status = ApiOperationStatus.Running,
                Stage = "running",
                ApiTokenId = tokenId,
                RouteKey = $"test.expired-lease.{index}",
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                RequestHash = "request-hash",
                AttemptCount = 1,
                LeaseOwner = "worker-a",
                LeaseExpiresAt = expiredAt,
                StartedAt = expiredAt.AddMinutes(-1)
            })
            .ToArray();

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ApiOperationService>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await SeedTokenAsync(context, tokenId);
        context.ApiOperations.AddRange(operations);
        await context.SaveChangesAsync();

        var renewAccepted = await service.RenewLeaseAsync(
            operations[0].Id,
            "worker-a",
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        var progressAccepted = await service.UpdateProgressAsync(
            operations[1].Id,
            "worker-a",
            "stale-progress",
            1,
            1,
            null,
            null,
            null,
            CancellationToken.None);
        var retryAccepted = await service.RetryOrFailAsync(
            operations[2].Id,
            "worker-a",
            5,
            "stale",
            "expired owner",
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        var completionAccepted = await service.CompleteAsync(
            operations[3].Id,
            "worker-a",
            "test",
            operations[3].Id.ToString(),
            CancellationToken.None);

        Assert.False(renewAccepted);
        Assert.False(progressAccepted);
        Assert.False(retryAccepted);
        Assert.False(completionAccepted);

        context.ChangeTracker.Clear();
        var persisted = await context.ApiOperations.AsNoTracking()
            .Where(item => operations.Select(operation => operation.Id).Contains(item.Id))
            .ToArrayAsync();
        Assert.All(persisted, operation =>
        {
            Assert.Equal(ApiOperationStatus.Running, operation.Status);
            Assert.Equal("worker-a", operation.LeaseOwner);
            Assert.True(operation.LeaseExpiresAt <= DateTimeOffset.UtcNow);
        });

        context.ApiOperations.RemoveRange(persisted);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task ClaimAsync_RecoversExpiredLeaseAndRejectsStaleOwnerCompletion()
    {
        await using var database = await IsolatedPostgresDatabase.CreateAsync(
            factory.DatabaseConnectionString);
        var operation = new ApiOperation
        {
            Kind = "test.lease",
            ApiTokenId = Guid.CreateVersion7(),
            RouteKey = "test.lease",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = "request-hash",
            NextAttemptAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        await using (var context = database.CreateContext())
        {
            await SeedTokenAsync(context, operation.ApiTokenId!.Value);
            context.ApiOperations.Add(operation);
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext())
        {
            await context.ApiOperations.Where(item => item.Id == operation.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.NextAttemptAt,
                        DateTimeOffset.UtcNow.AddSeconds(-1)));
            var service = new ApiOperationService(new EfApiOperationStore(context));
            var firstClaim = Assert.Single(await service.ClaimAsync(
                "worker-a", TimeSpan.FromMinutes(1), 1, CancellationToken.None));
            Assert.Equal(operation.Id, firstClaim.Id);
            Assert.Equal(1, firstClaim.AttemptCount);
            Assert.Empty(await service.ClaimAsync(
                "worker-b", TimeSpan.FromMinutes(1), 1, CancellationToken.None));
        }

        await using (var context = database.CreateContext())
        {
            await context.ApiOperations.Where(item => item.Id == operation.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.LeaseExpiresAt,
                        DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        await using var recoveryContext = database.CreateContext();
        var recovery = new ApiOperationService(new EfApiOperationStore(recoveryContext));
        var recovered = Assert.Single(await recovery.ClaimAsync(
            "worker-b", TimeSpan.FromMinutes(1), 1, CancellationToken.None));
        Assert.Equal(2, recovered.AttemptCount);
        Assert.False(await recovery.RetryOrFailAsync(
            operation.Id,
            "worker-a",
            5,
            "stale",
            "stale owner",
            TimeSpan.FromMinutes(1),
            CancellationToken.None));
        Assert.False(await recovery.UpdateProgressAsync(
            operation.Id,
            "worker-a",
            "stale",
            1,
            1,
            null,
            null,
            null,
            CancellationToken.None));
        Assert.True(await recovery.UpdateProgressAsync(
            operation.Id,
            "worker-b",
            "verified",
            1,
            1,
            null,
            null,
            null,
            CancellationToken.None));
        Assert.False(await recovery.CompleteAsync(
            operation.Id, "worker-a", null, null, CancellationToken.None));
        Assert.True(await recovery.CompleteAsync(
            operation.Id, "worker-b", "test", operation.Id.ToString(), CancellationToken.None));
    }

    [Fact]
    public async Task DatabaseClock_PreventsEarlyClaimAndBoundsLeaseExtensionAcrossWorkers()
    {
        await using var database = await IsolatedPostgresDatabase.CreateAsync(
            factory.DatabaseConnectionString);
        var operation = new ApiOperation
        {
            Kind = "test.database-clock",
            ApiTokenId = Guid.CreateVersion7(),
            RouteKey = "test.database-clock",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = "request-hash"
        };

        await using (var context = database.CreateContext())
        {
            await SeedTokenAsync(context, operation.ApiTokenId!.Value);
            context.ApiOperations.Add(operation);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ApiOperations"
                SET "NextAttemptAt" = CURRENT_TIMESTAMP + INTERVAL '5 minutes'
                WHERE "Id" = {operation.Id}
                """);
        }

        await using (var skewedWorkerContext = database.CreateContext())
        {
            var skewedWorker = new ApiOperationService(new EfApiOperationStore(skewedWorkerContext));
            Assert.Empty(await skewedWorker.ClaimAsync(
                "worker-with-fast-host-clock",
                TimeSpan.FromHours(6),
                1,
                CancellationToken.None));
        }

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ApiOperations"
                SET "NextAttemptAt" = CURRENT_TIMESTAMP - INTERVAL '1 second'
                WHERE "Id" = {operation.Id}
                """);
        }

        await using (var ownerContext = database.CreateContext())
        {
            var owner = new ApiOperationService(new EfApiOperationStore(ownerContext));
            Assert.Single(await owner.ClaimAsync(
                "worker-a", TimeSpan.FromMinutes(1), 1, CancellationToken.None));
            Assert.True(await owner.RenewLeaseAsync(
                operation.Id, "worker-a", TimeSpan.FromMinutes(2), CancellationToken.None));
        }

        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.ApiOperations.AsNoTracking()
            .SingleAsync(item => item.Id == operation.Id);
        var databaseNow = await verificationContext.Database
            .SqlQueryRaw<DateTimeOffset>("SELECT CURRENT_TIMESTAMP AS \"Value\"")
            .SingleAsync();
        Assert.InRange(
            persisted.LeaseExpiresAt!.Value,
            databaseNow.AddSeconds(110),
            databaseNow.AddSeconds(130));

        var secondWorker = new ApiOperationService(new EfApiOperationStore(verificationContext));
        Assert.Empty(await secondWorker.ClaimAsync(
            "worker-b", TimeSpan.FromMinutes(1), 1, CancellationToken.None));
    }

    [Fact]
    public async Task Worker_ExecutesOperationsInSeparateScopesAndPersistsTerminalState()
    {
        var firstOperation = await CreateOperationAsync("test.complete");
        var secondOperation = await CreateOperationAsync("test.complete");
        var expectedIds = new[] { firstOperation.Id, secondOperation.Id };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var current = await context.ApiOperations.AsNoTracking()
                .Where(item => expectedIds.Contains(item.Id))
                .ToArrayAsync();
            if (current.Length == 2 && current.All(item => item.Status == ApiOperationStatus.Succeeded))
            {
                Assert.All(current, operation =>
                {
                    Assert.Equal("completed", operation.Stage);
                    Assert.Equal(1, operation.AttemptCount);
                    Assert.Null(operation.LeaseOwner);
                    Assert.NotNull(operation.CompletedAt);
                });

                var recorder = scope.ServiceProvider
                    .GetRequiredService<Tests.Api.Fixtures.ApiOperationHandlerExecutionRecorder>();
                var executions = recorder.Snapshot()
                    .Where(execution => expectedIds.Contains(execution.OperationId))
                    .ToArray();
                var firstExecution = Assert.Single(
                    executions, execution => execution.OperationId == firstOperation.Id);
                var secondExecution = Assert.Single(
                    executions, execution => execution.OperationId == secondOperation.Id);
                Assert.NotEqual(firstExecution.HandlerInstanceId, secondExecution.HandlerInstanceId);
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The API operation worker did not complete the pending operation.");
    }

    [Fact]
    public async Task ConcurrentBeginAsync_ReusesSinglePersistedOperation()
    {
        var tokenId = Guid.CreateVersion7();
        var key = Guid.NewGuid().ToString("N");

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedTokenAsync(seedContext, tokenId);
        }

        async Task<Guid> BeginAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IdempotencyService>();
            var result = await service.BeginAsync(
                tokenId,
                "test.complete",
                key,
                "request-hash",
                CancellationToken.None);
            return result.Operation.Id;
        }

        var ids = await Task.WhenAll(BeginAsync(), BeginAsync());

        Assert.Equal(ids[0], ids[1]);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await context.ApiOperations.CountAsync(item =>
            item.ApiTokenId == tokenId && item.RouteKey == "test.complete" && item.IdempotencyKey == key));
    }

    [Fact]
    public async Task ConcurrentTeamLabSubmission_UsesBrowserActorAsIdempotencyIdentity()
    {
        var actorId = Guid.CreateVersion7();
        var otherActorId = Guid.CreateVersion7();
        var scopeId = Guid.CreateVersion7();
        var key = Guid.NewGuid().ToString("N");
        var route = $"POST:/api/open/v1/teamlab/scopes/{scopeId:D}/archive#scope:{scopeId:D}";

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedUserAsync(seedContext, actorId);
            await SeedUserAsync(seedContext, otherActorId);
        }

        async Task<Guid> SubmitAsync(Guid actor, string hash)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ITeamLabRuntimeOperationSubmissionStore>();
            var result = await store.SubmitAsync(new TeamLabRuntimeOperationSubmission(
                null,
                actor,
                scopeId,
                route,
                key,
                hash,
                "teamlab-scope",
                scopeId.ToString("D"),
                new TeamLabRuntimeOperationJob
                {
                    Kind = TeamLabRuntimeOperationKind.RolloutArchive,
                    PayloadHash = $"sha256:{hash}"
                }), CancellationToken.None);
            return result.Operation.Id;
        }

        var ids = await Task.WhenAll(
            SubmitAsync(actorId, "same-request-hash"),
            SubmitAsync(actorId, "same-request-hash"));

        Assert.Equal(ids[0], ids[1]);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            SubmitAsync(actorId, "changed-request-hash"));

        var otherActorOperation = await SubmitAsync(otherActorId, "same-request-hash");
        Assert.NotEqual(ids[0], otherActorOperation);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await context.ApiOperations.CountAsync(item =>
            item.ApiTokenId == null && item.RouteKey == route && item.IdempotencyKey == key));
    }

    [Fact]
    public async Task OperationsApi_OnlyReturnsOperationsOwnedByCurrentToken()
    {
        var issued = await IssueOperationsTokenAsync();
        var own = await CreateOperationAsync("test.complete", issued.TokenId);
        var foreign = await CreateOperationAsync("test.complete", Guid.CreateVersion7());
        var client = factory.CreateClient();

        using var ownRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/open/v1/operations/{own.Id}");
        ownRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issued.PlainTextToken);
        using var ownResponse = await client.SendAsync(ownRequest);

        using var foreignRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/open/v1/operations/{foreign.Id}");
        foreignRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issued.PlainTextToken);
        using var foreignResponse = await client.SendAsync(foreignRequest);

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
    }

    private async Task<ApiOperation> CreateOperationAsync(string kind, Guid? tokenId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actualTokenId = tokenId ?? Guid.CreateVersion7();
        await SeedTokenAsync(context, actualTokenId);
        var service = scope.ServiceProvider.GetRequiredService<IdempotencyService>();
        var result = await service.BeginAsync(
            actualTokenId,
            kind,
            Guid.NewGuid().ToString("N"),
            "request-hash",
            CancellationToken.None);
        return result.Operation;
    }

    private static async Task SeedTokenAsync(AppDbContext context, Guid tokenId)
    {
        if (await context.ApiTokens.AnyAsync(token => token.Id == tokenId))
            return;

        var creatorId = Guid.CreateVersion7();
        var suffix = tokenId.ToString("N")[^7..];
        context.Users.Add(new UserInfo
        {
            Id = creatorId,
            UserName = $"op-seed-{suffix}",
            NormalizedUserName = $"OP-SEED-{suffix.ToUpperInvariant()}",
            Email = $"op-seed-{suffix}@example.test",
            NormalizedEmail = $"OP-SEED-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Teacher,
            RegisterTimeUtc = DateTimeOffset.UtcNow
        });
        context.ApiTokens.Add(new ApiTokenEntity
        {
            Id = tokenId,
            Name = "operation test token",
            CreatorId = creatorId,
            SecretHash = new byte[32],
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedUserAsync(AppDbContext context, Guid userId)
    {
        if (await context.Users.AnyAsync(user => user.Id == userId))
            return;

        var suffix = userId.ToString("N")[^7..];
        context.Users.Add(new UserInfo
        {
            Id = userId,
            UserName = $"opactor-{suffix}",
            NormalizedUserName = $"OPACTOR-{suffix.ToUpperInvariant()}",
            Email = $"operation-actor-{suffix}@example.test",
            NormalizedEmail = $"OPERATION-ACTOR-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Teacher,
            RegisterTimeUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private async Task<IssuedOperationsToken> IssueOperationsTokenAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = scope.ServiceProvider.GetRequiredService<ApiTokenIssuer>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new UserInfo
        {
            Id = Guid.CreateVersion7(),
            UserName = $"op-{suffix}",
            NormalizedUserName = $"OP-{suffix.ToUpperInvariant()}",
            Email = $"operation-{suffix}@example.test",
            NormalizedEmail = $"OPERATION-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Teacher,
            RegisterTimeUtc = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var issued = await issuer.IssueAsync(
            new ActorContext(user.Id, user.Role),
            new IssueApiTokenCommand(
                "operations",
                [ApiTokenScopes.OperationsRead],
                [],
                60,
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);
        return new IssuedOperationsToken(issued.PlainTextToken, issued.Token.Id);
    }

    private sealed record IssuedOperationsToken(string PlainTextToken, Guid TokenId);
}
