using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Identity.Domain;
using GZCTF.Modules.Identity.Infrastructure;
using GZCTF.Utils;
using Xunit;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Test.UnitTests.Security;

public class ApiTokenIssuerTests
{
    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task IssueAsync_ReturnsSecretOnceAndStoresOnlyDigest()
    {
        var store = new InMemoryApiTokenStore();
        var issuer = new ApiTokenIssuer(store, new ApiTokenSecretHasher());

        var result = await issuer.IssueAsync(
            new ActorContext(TeacherId, Role.Teacher),
            new IssueApiTokenCommand(
                "image publisher",
                [ApiTokenScopes.ImagesWrite, ApiTokenScopes.OperationsRead],
                [],
                60,
                DateTimeOffset.UtcNow.AddDays(30)),
            CancellationToken.None);

        Assert.StartsWith($"gzctf_pat_{result.Token.Id:N}.", result.PlainTextToken);
        Assert.Equal(32, result.Token.SecretHash.Length);
        Assert.DoesNotContain(result.PlainTextToken, JsonSerializer.Serialize(result.Token));
        Assert.DoesNotContain(result.PlainTextToken, Convert.ToHexString(result.Token.SecretHash));
        Assert.Same(result.Token, store.Token);
    }

    [Fact]
    public async Task ValidateAsync_RejectsChangedRevokedAndExpiredSecrets()
    {
        var store = new InMemoryApiTokenStore();
        var hasher = new ApiTokenSecretHasher();
        var issuer = new ApiTokenIssuer(store, hasher);
        var validator = new ApiTokenValidator(store, hasher);

        var issued = await issuer.IssueAsync(
            new ActorContext(TeacherId, Role.Teacher),
            new IssueApiTokenCommand(
                "reader",
                [ApiTokenScopes.ImagesRead],
                [],
                30,
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);

        Assert.True((await validator.ValidateAsync(
            issued.PlainTextToken, CancellationToken.None)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            issued.PlainTextToken + "x", CancellationToken.None)).Succeeded);

        store.Token!.RevokedAt = DateTimeOffset.UtcNow;
        Assert.False((await validator.ValidateAsync(
            issued.PlainTextToken, CancellationToken.None)).Succeeded);

        store.Token.RevokedAt = null;
        store.Token.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.False((await validator.ValidateAsync(
            issued.PlainTextToken, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task IssueAsync_RejectsScopesOutsideTeacherGrantSet()
    {
        var issuer = new ApiTokenIssuer(new InMemoryApiTokenStore(), new ApiTokenSecretHasher());

        await Assert.ThrowsAsync<ApiTokenScopeException>(() => issuer.IssueAsync(
            new ActorContext(TeacherId, Role.Teacher),
            new IssueApiTokenCommand("admin", ["admin:write"], [], 60, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_RejectsTokenAfterCreatorIsDemoted()
    {
        var store = new InMemoryApiTokenStore();
        var hasher = new ApiTokenSecretHasher();
        var issuer = new ApiTokenIssuer(store, hasher);
        var validator = new ApiTokenValidator(store, hasher);
        var issued = await issuer.IssueAsync(
            new ActorContext(TeacherId, Role.Teacher),
            new IssueApiTokenCommand("writer", [ApiTokenScopes.ImagesWrite], [], 60, null),
            CancellationToken.None);

        store.CreatorRole = Role.Student;

        Assert.False((await validator.ValidateAsync(
            issued.PlainTextToken, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task IssueAsync_NormalizesResourcesBeforeDeduplication()
    {
        var issuer = new ApiTokenIssuer(new InMemoryApiTokenStore(), new ApiTokenSecretHasher());

        var issued = await issuer.IssueAsync(
            new ActorContext(TeacherId, Role.Teacher),
            new IssueApiTokenCommand(
                "resource",
                [ApiTokenScopes.ImagesRead],
                [new("image", "template-1"), new(" IMAGE ", " template-1 ")],
                60,
                null),
            CancellationToken.None);

        var resource = Assert.Single(issued.Token.Resources);
        Assert.Equal("image", resource.ResourceType);
        Assert.Equal("template-1", resource.ResourceId);
    }

    [Fact]
    public async Task IssueAsync_RejectsOversizedResourceGrant()
    {
        var issuer = new ApiTokenIssuer(new InMemoryApiTokenStore(), new ApiTokenSecretHasher());

        await Assert.ThrowsAsync<ArgumentException>(() => issuer.IssueAsync(
            new ActorContext(TeacherId, Role.Teacher),
            new IssueApiTokenCommand(
                "resource",
                [ApiTokenScopes.ImagesRead],
                [new("image", new string('x', 129))],
                60,
                null),
            CancellationToken.None));
    }

    private sealed class InMemoryApiTokenStore : IApiTokenStore
    {
        public ApiTokenEntity? Token { get; private set; }
        public Role CreatorRole { get; set; } = Role.Teacher;

        public Task AddAsync(ApiTokenEntity token, CancellationToken cancellationToken)
        {
            Token = token;
            return Task.CompletedTask;
        }

        public Task<ApiTokenEntity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Token?.Id == id ? Token : null);

        public Task<ApiTokenValidationRecord?> FindForValidationAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Token?.Id == id
                ? new ApiTokenValidationRecord(Token, CreatorRole)
                : null);

        public Task<IReadOnlyList<ApiTokenEntity>> ListAsync(Guid? creatorId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApiTokenEntity>>(
                Token is not null && (creatorId is null || Token.CreatorId == creatorId) ? [Token] : []);

        public Task<bool> RevokeAsync(Guid id, Guid actorId, bool allowAny, CancellationToken cancellationToken)
        {
            if (Token is null || Token.Id != id || (!allowAny && Token.CreatorId != actorId))
                return Task.FromResult(false);

            Token.RevokedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }

        public Task RecordUsageAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken)
        {
            if (Token?.Id == id)
                Token.LastUsedAt = usedAt;
            return Task.CompletedTask;
        }
    }
}
