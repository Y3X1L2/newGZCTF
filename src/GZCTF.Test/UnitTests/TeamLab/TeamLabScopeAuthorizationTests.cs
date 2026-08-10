using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabScopeAuthorizationTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"scope-auth-{Guid.NewGuid():N}")
            .Options;
        var context = new AppDbContext(options);
        var scope = new TeamLabControlScope { Key = "accept", DisplayName = "Acceptance" };
        context.TeamLabControlScopes.Add(scope);
        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task ResourceGrantPolicy_AllowsAdministratorWildcardOnly()
    {
        using var context = CreateContext();
        var policy = new TeamLabScopeApiTokenResourceGrantPolicy(context);

        Assert.True(await policy.CanGrantAsync(
            new GZCTF.Modules.Identity.Application.ActorContext(Guid.NewGuid(), Role.Admin),
            "*",
            CancellationToken.None));
        Assert.False(await policy.CanGrantAsync(
            new GZCTF.Modules.Identity.Application.ActorContext(Guid.NewGuid(), Role.Teacher),
            "*",
            CancellationToken.None));

        var scope = context.TeamLabControlScopes.Single();
        scope.IsArchived = true;
        await context.SaveChangesAsync();
        Assert.False(await policy.CanGrantAsync(
            new GZCTF.Modules.Identity.Application.ActorContext(Guid.NewGuid(), Role.Admin),
            scope.Id.ToString("D"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ExactGrant_SatisfiesReadableAndWritable()
    {
        using var context = CreateContext();
        var scope = context.TeamLabControlScopes.Single();
        var service = new TeamLabScopeAuthorizationService(context);
        var tokenId = Guid.NewGuid();
        context.ApiTokenResourceGrants.Add(new GZCTF.Modules.Identity.Domain.ApiTokenResourceGrant
        {
            TokenId = tokenId,
            ResourceType = "teamlab-scope",
            ResourceId = scope.Id.ToString("D")
        });
        await context.SaveChangesAsync();

        await service.RequireReadableAsync(scope.Id, tokenId, administrator: false, CancellationToken.None);
        await service.RequireWritableAsync(scope.Id, tokenId, administrator: false, CancellationToken.None);
    }

    [Fact]
    public async Task WildcardGrant_SatisfiesAnyScope()
    {
        using var context = CreateContext();
        var service = new TeamLabScopeAuthorizationService(context);
        var tokenId = Guid.NewGuid();
        context.ApiTokenResourceGrants.Add(new GZCTF.Modules.Identity.Domain.ApiTokenResourceGrant
        {
            TokenId = tokenId,
            ResourceType = "teamlab-scope",
            ResourceId = "*"
        });
        await context.SaveChangesAsync();

        var otherScopeId = Guid.NewGuid();
        context.TeamLabControlScopes.Add(new TeamLabControlScope
        {
            Id = otherScopeId,
            Key = "other",
            DisplayName = "Other"
        });
        await context.SaveChangesAsync();

        await service.RequireReadableAsync(otherScopeId, tokenId, administrator: false, CancellationToken.None);
        await service.RequireWritableAsync(otherScopeId, tokenId, administrator: false, CancellationToken.None);
    }

    [Fact]
    public async Task NoGrant_ThrowsNotFound_EvenWhenAdministratorFlagFalse()
    {
        using var context = CreateContext();
        var scope = context.TeamLabControlScopes.Single();
        var service = new TeamLabScopeAuthorizationService(context);
        var tokenId = Guid.NewGuid();

        var readable = await Record.ExceptionAsync(() =>
            service.RequireReadableAsync(scope.Id, tokenId, administrator: false, CancellationToken.None));
        Assert.IsType<TeamLabApiContractException>(readable);

        var writable = await Record.ExceptionAsync(() =>
            service.RequireWritableAsync(scope.Id, tokenId, administrator: false, CancellationToken.None));
        Assert.IsType<TeamLabApiContractException>(writable);
    }

    [Fact]
    public async Task ArchivedScope_RejectsWritable_ButAllowsReadable()
    {
        using var context = CreateContext();
        var scope = context.TeamLabControlScopes.Single();
        scope.IsArchived = true;
        await context.SaveChangesAsync();
        var service = new TeamLabScopeAuthorizationService(context);
        var tokenId = Guid.NewGuid();
        context.ApiTokenResourceGrants.Add(new GZCTF.Modules.Identity.Domain.ApiTokenResourceGrant
        {
            TokenId = tokenId,
            ResourceType = "teamlab-scope",
            ResourceId = scope.Id.ToString("D")
        });
        await context.SaveChangesAsync();

        await service.RequireReadableAsync(scope.Id, tokenId, administrator: false, CancellationToken.None);
        var writable = await Record.ExceptionAsync(() =>
            service.RequireWritableAsync(scope.Id, tokenId, administrator: false, CancellationToken.None));
        var contractException = Assert.IsType<TeamLabApiContractException>(writable);
        Assert.Equal("scope_archived", contractException.Code);
        Assert.Equal(409, contractException.StatusCode);
    }

    [Fact]
    public async Task ArchivedScope_WithoutGrant_StillHidesExistence()
    {
        using var context = CreateContext();
        var scope = context.TeamLabControlScopes.Single();
        scope.IsArchived = true;
        await context.SaveChangesAsync();
        var service = new TeamLabScopeAuthorizationService(context);
        var tokenId = Guid.NewGuid();

        var writable = await Record.ExceptionAsync(() =>
            service.RequireWritableAsync(scope.Id, tokenId, administrator: false, CancellationToken.None));
        var contractException = Assert.IsType<TeamLabApiContractException>(writable);
        Assert.Equal("scope_not_found", contractException.Code);
        Assert.Equal(404, contractException.StatusCode);
    }

    [Fact]
    public async Task ListReadableScopes_RespectsGrants()
    {
        using var context = CreateContext();
        var tokenId = Guid.NewGuid();
        var first = context.TeamLabControlScopes.Single();
        var second = new TeamLabControlScope { Key = "second", DisplayName = "Second" };
        context.TeamLabControlScopes.Add(second);
        context.ApiTokenResourceGrants.Add(new GZCTF.Modules.Identity.Domain.ApiTokenResourceGrant
        {
            TokenId = tokenId,
            ResourceType = "teamlab-scope",
            ResourceId = first.Id.ToString("D")
        });
        await context.SaveChangesAsync();

        var service = new TeamLabScopeAuthorizationService(context);
        var readable = await service.ListReadableScopesAsync(tokenId, administrator: false, CancellationToken.None);
        Assert.Contains(first.Id, readable);
        Assert.DoesNotContain(second.Id, readable);
    }
}
