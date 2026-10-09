using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class OpenTeamLabRetirementApiTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task ArchivedParent_StillAllowsAuthorizedRetirementButRejectsOtherWritesAndForeignReaders()
    {
        Guid scopeId, foreignId, topologyId, releaseId, runtimeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actor = Actor();
            var current = new TeamLabControlScope { Key = "retire-" + Guid.NewGuid().ToString("N")[..8], DisplayName = "retirement" };
            var foreign = new TeamLabControlScope { Key = "foreign-" + Guid.NewGuid().ToString("N")[..8], DisplayName = "foreign" };
            var topology = new TeamLabTopology { Name = "retirement", OwnerUserId = actor.Id, ControlScope = current, SchemaVersion = 2 };
            var canonical = TeamLabReleaseCodec.Encode(2, new TeamLabTopologyDefinitionModel("retirement", [], [], []));
            var release = new TeamLabTopologyRelease
            {
                Topology = topology, ControlScope = current, Version = 1, SchemaVersion = 2,
                CanonicalJson = canonical, ContentHash = TeamLabReleaseCodec.ComputeContentHash(2, canonical)
            };
            var runtime = new TeamLabRuntime
            {
                TopologyReleaseId = release.Id, ControlScopeId = current.Id, CreatedById = actor.Id,
                Status = TeamLabRuntimeStatus.Destroyed
            };
            context.AddRange(actor, current, foreign, release, runtime);
            await context.SaveChangesAsync();
            scopeId = current.Id;
            foreignId = foreign.Id;
            topologyId = topology.PublicId;
            releaseId = release.Id;
            runtimeId = runtime.PublicId;
        }
        // Grants are issued while the scope is active; retirement does not widen the token.
        var writer = await TokenAsync(scopeId, [ApiTokenScopes.TeamLabTopologiesWrite, ApiTokenScopes.TeamLabRuntimesWrite]);
        var readOnly = await TokenAsync(scopeId, [ApiTokenScopes.TeamLabTopologiesRead]);
        var foreignWriter = await TokenAsync(foreignId, [ApiTokenScopes.TeamLabTopologiesWrite]);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TeamLabControlScopeService>().ArchiveAsync(scopeId, default);

        using var client = factory.CreateClient();
        var path = $"/api/open/v1/teamlab/topologies/{topologyId:D}/releases/{releaseId:D}/archive";
        client.DefaultRequestHeaders.Authorization = new("Bearer", readOnly);
        using (var denied = await client.PostAsync(path, null))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", foreignWriter);
        using (var denied = await client.PostAsync(path, null))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", writer);
        using (var retired = await client.PostAsync(path, null))
            Assert.Equal(HttpStatusCode.NoContent, retired.StatusCode);
        using (var repeated = await client.PostAsync(path, null))
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);

        using (var request = JsonRequest("/api/open/v1/teamlab/runtimes", new CreateTeamLabRuntimeModel(releaseId, null, null, null)))
        using (var blocked = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using (var request = JsonRequest($"/api/open/v1/teamlab/runtimes/{runtimeId:D}/reset", new ResetTeamLabRuntimeModel(null)))
        using (var blocked = await client.SendAsync(request))
        {
            // Reset submission keeps its asynchronous contract; its operation must fail
            // before a deployment ticket or physical cleanup can be admitted.
            Assert.Equal(HttpStatusCode.Accepted, blocked.StatusCode);
            using var operation = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
            await AssertResetRejectedAsync(operation.RootElement.GetProperty("id").GetGuid());
        }
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"/api/open/v1/teamlab/preparations/releases/{releaseId:D}"))
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var blocked = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        }
        await using var verification = factory.Services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.TeamLabTopologyReleases.SingleAsync(item => item.Id == releaseId)).IsArchived);
        Assert.Equal(TeamLabRuntimeStatus.Destroyed, (await db.TeamLabRuntimes.SingleAsync(item => item.PublicId == runtimeId)).Status);
        Assert.False(await db.DeploymentQueueTickets.AnyAsync(item => item.TeamLabRuntimeId ==
            db.TeamLabRuntimes.Where(runtime => runtime.PublicId == runtimeId).Select(runtime => runtime.Id).First()));
    }

    private static HttpRequestMessage JsonRequest(string path, object value)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(value) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    private async Task AssertResetRejectedAsync(Guid operationId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operation = await context.ApiOperations.AsNoTracking().SingleAsync(item => item.Id == operationId);
            if (operation.Status == ApiOperationStatus.Failed)
            {
                Assert.Equal("release_archived", operation.ErrorCode);
                Assert.Null(operation.DeploymentQueueTicketId);
                return;
            }
            Assert.NotEqual(ApiOperationStatus.Succeeded, operation.Status);
            await Task.Delay(100);
        }
        Assert.Fail("Retired reset did not reach its terminal rejection within ten seconds.");
    }

    private async Task<string> TokenAsync(Guid scopeId, IReadOnlyCollection<string> scopes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = Actor();
        context.Users.Add(actor);
        await context.SaveChangesAsync();
        var issued = await scope.ServiceProvider.GetRequiredService<ApiTokenIssuer>().IssueAsync(
            new ActorContext(actor.Id, actor.Role),
            new("retirement test", scopes, [new("teamlab-scope", scopeId.ToString("D"))], 120,
                DateTimeOffset.UtcNow.AddHours(1)), default);
        return issued.PlainTextToken;
    }

    private static UserInfo Actor()
    {
        var name = "ret-" + Guid.NewGuid().ToString("N")[..8];
        return new UserInfo
        {
            UserName = name, NormalizedUserName = name.ToUpperInvariant(),
            Role = Role.Admin, RegisterTimeUtc = DateTimeOffset.UtcNow
        };
    }
}
