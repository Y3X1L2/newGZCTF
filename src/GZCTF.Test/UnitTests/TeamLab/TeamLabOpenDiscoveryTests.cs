using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Modules.Identity.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabOpenDiscoveryTests
{
    [Fact]
    public async Task ListRuntimes_UsesDirectScopeGrantAndStableCursor()
    {
        await using var context = CreateContext();
        var tokenId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var firstScope = Scope("first");
        var secondScope = Scope("second");
        context.TeamLabControlScopes.AddRange(firstScope, secondScope);
        context.ApiTokenResourceGrants.Add(new ApiTokenResourceGrant
        {
            TokenId = tokenId,
            ResourceType = "teamlab-scope",
            ResourceId = firstScope.Id.ToString("D")
        });
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var expectedFirst = Runtime(firstScope.Id, actorId, "00000000-0000-0000-0000-000000000030", createdAt, "match-a");
        var expectedSecond = Runtime(firstScope.Id, actorId, "00000000-0000-0000-0000-000000000020", createdAt, "match-b");
        var hidden = Runtime(secondScope.Id, actorId, "00000000-0000-0000-0000-000000000040", createdAt, "match-c");
        context.TeamLabRuntimes.AddRange(expectedFirst, expectedSecond, hidden);
        await context.SaveChangesAsync();

        var service = Service(context);
        var firstPage = await service.ListRuntimesAsync(
            tokenId, false, null, null, null, null, 1, CancellationToken.None);
        var first = Assert.Single(firstPage.Items);
        Assert.Equal(expectedFirst.PublicId, first.Id);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await service.ListRuntimesAsync(
            tokenId, false, null, null, null, firstPage.NextCursor, 1, CancellationToken.None);
        var second = Assert.Single(secondPage.Items);
        Assert.Equal(expectedSecond.PublicId, second.Id);
        Assert.Null(secondPage.NextCursor);
        Assert.DoesNotContain(firstPage.Items.Concat(secondPage.Items), item => item.Id == hidden.PublicId);

        var otherTokenPage = await service.ListRuntimesAsync(
            Guid.CreateVersion7(), false, null, null, null, null, 50, CancellationToken.None);
        Assert.Empty(otherTokenPage.Items);
    }

    [Fact]
    public async Task ListRuntimes_AppliesScopeReferenceAndStatusFilters()
    {
        await using var context = CreateContext();
        var tokenId = Guid.CreateVersion7();
        var scope = Scope("filter");
        var otherScope = Scope("other");
        context.TeamLabControlScopes.AddRange(scope, otherScope);
        context.ApiTokenResourceGrants.Add(new ApiTokenResourceGrant
        {
            TokenId = tokenId,
            ResourceType = "teamlab-scope",
            ResourceId = scope.Id.ToString("D")
        });
        var expected = Runtime(scope.Id, Guid.CreateVersion7(), null, DateTimeOffset.UtcNow, "external-42");
        expected.Status = TeamLabRuntimeStatus.Running;
        var wrongStatus = Runtime(scope.Id, Guid.CreateVersion7(), null, DateTimeOffset.UtcNow, "external-42");
        wrongStatus.Status = TeamLabRuntimeStatus.Failed;
        context.TeamLabRuntimes.AddRange(expected, wrongStatus);
        await context.SaveChangesAsync();

        var service = Service(context);
        var page = await service.ListRuntimesAsync(
            tokenId,
            false,
            scope.Id,
            " external-42 ",
            TeamLabRuntimeStatus.Running,
            null,
            50,
            CancellationToken.None);
        Assert.Equal(expected.PublicId, Assert.Single(page.Items).Id);

        var inaccessible = await service.ListRuntimesAsync(
            tokenId, false, otherScope.Id, null, null, null, 50, CancellationToken.None);
        Assert.Empty(inaccessible.Items);
    }

    [Fact]
    public async Task ListAccessGrants_ReturnsOnlyManageableMetadataWithoutSecrets()
    {
        await using var context = CreateContext();
        var tokenId = Guid.CreateVersion7();
        var scope = Scope("access");
        var runtime = Runtime(scope.Id, Guid.CreateVersion7(), null, DateTimeOffset.UtcNow, null);
        runtime.Generation = 2;
        context.TeamLabControlScopes.Add(scope);
        context.ApiTokenResourceGrants.Add(new ApiTokenResourceGrant
        {
            TokenId = tokenId,
            ResourceType = "teamlab-scope",
            ResourceId = scope.Id.ToString("D")
        });
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        var expected = Grant(runtime.Id, generation: 2, revoked: false);
        context.TeamLabAccessGrants.AddRange(
            expected,
            Grant(runtime.Id, generation: 2, revoked: true),
            Grant(runtime.Id, generation: 1, revoked: false));
        await context.SaveChangesAsync();

        var service = Service(context);
        var grants = await service.ListAccessGrantsAsync(runtime.PublicId, CancellationToken.None);
        var grant = Assert.Single(grants);
        Assert.Equal(expected.PublicId, grant.Id);
        var json = JsonSerializer.Serialize(grant);
        Assert.DoesNotContain("ProtectedPrivateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProtectedServerPrivateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProtectedDownloadToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DownloadTokenHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfigurationDownloadUrl", json, StringComparison.OrdinalIgnoreCase);

    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"teamlab-open-discovery-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static TeamLabOpenDiscoveryService Service(AppDbContext context) =>
        new(context, new TeamLabScopeAuthorizationService(context), new TeamLabAuthorizationService(context));

    private static TeamLabControlScope Scope(string key) => new()
    {
        Id = Guid.CreateVersion7(),
        Key = key,
        DisplayName = key
    };

    private static TeamLabRuntime Runtime(
        Guid scopeId,
        Guid actorId,
        string? publicId,
        DateTimeOffset createdAt,
        string? externalReference) => new()
    {
        PublicId = publicId is null ? Guid.CreateVersion7() : Guid.Parse(publicId),
        ControlScopeId = scopeId,
        TopologyReleaseId = Guid.CreateVersion7(),
        CreatedById = actorId,
        ExternalReference = externalReference,
        CreateRequestHash = Guid.NewGuid().ToString("N"),
        CreatedAt = createdAt,
        Status = TeamLabRuntimeStatus.Pending
    };

    private static TeamLabAccessGrant Grant(int runtimeId, int generation, bool revoked) => new()
    {
        RuntimeId = runtimeId,
        Generation = generation,
        Revoked = revoked,
        ClientAddress = "10.0.0.2/32",
        Endpoint = "vpn.example.test:51820",
        AllowedIps = "10.0.0.0/24",
        Dns = "10.0.0.1",
        PublicKey = "public",
        ServerPublicKey = "server-public",
        ProtectedPrivateKey = "private-secret",
        ProtectedServerPrivateKey = "server-private-secret",
        DownloadTokenHash = "token-hash",
        ProtectedDownloadToken = "download-secret",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
    };
}
