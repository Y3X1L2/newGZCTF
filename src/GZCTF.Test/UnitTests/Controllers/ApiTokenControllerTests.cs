using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Modules.Identity.Api;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Identity.Infrastructure;
using GZCTF.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Test.UnitTests.Controllers;

public class ApiTokenControllerTests
{
    [Fact]
    public async Task Teacher_CannotRevokeAnotherUsersToken()
    {
        var teacher = CreateUser(Role.Teacher);
        var otherUserId = Guid.CreateVersion7();
        var store = new OwnershipStore(new ApiTokenEntity
        {
            Name = "other",
            CreatorId = otherUserId,
            SecretHash = new byte[32]
        });
        var controller = CreateController(teacher, store);

        var result = await controller.Revoke(store.Token.Id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Null(store.Token.RevokedAt);
    }

    [Fact]
    public async Task Administrator_CanRevokeAnotherUsersToken()
    {
        var administrator = CreateUser(Role.Admin);
        var store = new OwnershipStore(new ApiTokenEntity
        {
            Name = "teacher token",
            CreatorId = Guid.CreateVersion7(),
            SecretHash = new byte[32]
        });
        var controller = CreateController(administrator, store);

        var result = await controller.Revoke(store.Token.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.NotNull(store.Token.RevokedAt);
    }

    private static ApiTokensController CreateController(UserInfo user, IApiTokenStore store)
    {
        var userStore = new Mock<IUserStore<UserInfo>>();
        var userManager = new Mock<UserManager<UserInfo>>(
            userStore.Object,
            null!,
            null!,
            Array.Empty<IUserValidator<UserInfo>>(),
            Array.Empty<IPasswordValidator<UserInfo>>(),
            null!,
            null!,
            null!,
            null!);
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);

        return new ApiTokensController(
            userManager.Object,
            new ApiTokenIssuer(store, new ApiTokenSecretHasher()),
            store,
            NullLogger<ApiTokensController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity("test"))
                }
            }
        };
    }

    private static UserInfo CreateUser(Role role) => new()
    {
        Id = Guid.CreateVersion7(),
        UserName = role.ToString(),
        Email = $"{role.ToString().ToLowerInvariant()}@example.test",
        Role = role
    };

    private sealed class OwnershipStore(ApiTokenEntity token) : IApiTokenStore
    {
        public ApiTokenEntity Token { get; } = token;

        public Task AddAsync(ApiTokenEntity newToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApiTokenEntity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ApiTokenEntity?>(Token.Id == id ? Token : null);

        public Task<ApiTokenValidationRecord?> FindForValidationAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ApiTokenValidationRecord?>(Token.Id == id
                ? new ApiTokenValidationRecord(Token, Role.Teacher)
                : null);

        public Task<IReadOnlyList<ApiTokenEntity>> ListAsync(Guid? creatorId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApiTokenEntity>>(
                creatorId is null || Token.CreatorId == creatorId ? [Token] : []);

        public Task<bool> RevokeAsync(Guid id, Guid actorId, bool allowAny, CancellationToken cancellationToken)
        {
            if (Token.Id != id || (!allowAny && Token.CreatorId != actorId))
                return Task.FromResult(false);

            Token.RevokedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }

        public Task RecordUsageAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
