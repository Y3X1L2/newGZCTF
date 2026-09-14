using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using GZCTF.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts.Execution;
using GZCTF.Utils;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class OpenTeamLabOperationsApiTests(GZCTFApplicationFactory factory)
{
    private static readonly JsonSerializerOptions ApiJsonOptions = CreateApiJsonOptions();

    [Fact]
    public async Task AssetFiles_UseScopedTokenAndStreamUploadsAndDownloads()
    {
        var gateway = new InMemoryAssetFileGateway();
        gateway.SetFile("/seed.txt", "seed"u8.ToArray());
        await using var host = CreateHost(gateway);
        using var client = host.CreateClient();
        var fixture = await SeedAsync(host.Services);
        var allowed = await IssueTokenAsync(
            host.Services,
            fixture.ScopeId,
            [ApiTokenScopes.TeamLabRemoteSessionsRead, ApiTokenScopes.TeamLabRemoteSessionsWrite]);
        var foreign = await IssueTokenAsync(
            host.Services,
            fixture.ForeignScopeId,
            [ApiTokenScopes.TeamLabRemoteSessionsRead]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", allowed.PlainTextToken);
        var basePath = $"/api/open/v1/teamlab/runtimes/{fixture.RuntimeId:D}/assets/{fixture.AssetId}/files";

        var list = await client.GetFromJsonAsync<OpenTeamLabAssetFileListModel>(
            $"{basePath}?generation=1&path=%2F");
        Assert.NotNull(list);
        Assert.Contains(list.Items, item => item.Name == "seed.txt" && item.Kind == "file");

        var payload = "streamed payload"u8.ToArray();
        using (var upload = new MultipartFormDataContent())
        {
            upload.Add(new StringContent("1"), "generation");
            upload.Add(new StringContent("/upload.bin"), "path");
            upload.Add(new ByteArrayContent(payload), "file", "upload.bin");
            using var response = await client.PostAsync($"{basePath}/upload", upload);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await client.GetAsync(
                   $"{basePath}/download?generation=1&path=%2Fupload.bin",
                   HttpCompletionOption.ResponseHeadersRead))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
        }

        using (var response = await client.PostAsJsonAsync(
                   $"{basePath}/directories",
                   new OpenCreateTeamLabAssetDirectoryModel(1, "/archive")))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var response = await client.PostAsJsonAsync(
                   $"{basePath}/move",
                   new OpenMoveTeamLabAssetFileModel(1, "/upload.bin", "/archive/upload.bin")))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var response = await client.DeleteAsync(
                   $"{basePath}?generation=1&path=%2Farchive%2Fupload.bin&recursive=false&confirmed=true"))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreign.PlainTextToken);
        using (var response = await client.GetAsync($"{basePath}?generation=1&path=%2F"))
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "scope_not_found");

        Assert.Equal(
            ["list", "upload", "download", "mkdir", "move", "delete"],
            gateway.Operations.ToArray());

        await using var verificationScope = host.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await context.ExternalApiRequestAudits.AnyAsync(item =>
            item.ApiTokenId == allowed.TokenId &&
            item.ActorUserId == allowed.ActorUserId &&
            EF.Functions.Like(item.RouteKey, "%/files%")));
    }

    [Fact]
    public async Task RemoteAudit_ReturnsScopedSummaryAndVerifiedEvidence()
    {
        await using var host = CreateHost(new InMemoryAssetFileGateway());
        using var client = host.CreateClient();
        var fixture = await SeedAsync(host.Services);
        var allowed = await IssueTokenAsync(
            host.Services,
            fixture.ScopeId,
            [ApiTokenScopes.TeamLabRemoteSessionsRead, ApiTokenScopes.TeamLabRemoteSessionsWrite]);
        var foreign = await IssueTokenAsync(
            host.Services,
            fixture.ForeignScopeId,
            [ApiTokenScopes.TeamLabRemoteSessionsRead, ApiTokenScopes.TeamLabRemoteSessionsWrite]);

        var basePath = $"/api/open/v1/teamlab/remote-sessions/{fixture.SessionId:D}/audit";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreign.PlainTextToken);
        using (var response = await client.PostAsync(basePath, null))
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "scope_not_found");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", allowed.PlainTextToken);
        using (var response = await client.PostAsync(basePath, null))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var summary = await client.GetFromJsonAsync<OpenTeamLabRemoteAuditSummaryModel>(basePath, ApiJsonOptions);
        Assert.NotNull(summary);
        Assert.Equal("ready", summary.State);
        var evidence = Assert.Single(summary.Evidence);

        using (var response = await client.GetAsync(
                   $"{basePath}/evidence/{evidence.Id}/download",
                   HttpCompletionOption.ResponseHeadersRead))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            await using var content = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(content);
            Assert.Equal(fixture.SessionId, document.RootElement.GetProperty("sessionId").GetGuid());
            Assert.Equal(fixture.OwnerUserId, document.RootElement.GetProperty("operatorId").GetGuid());
            Assert.False(document.RootElement.GetProperty("contentRecorded").GetBoolean());
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreign.PlainTextToken);
        using (var response = await client.GetAsync(basePath))
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "scope_not_found");

        await using var verificationScope = host.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await context.ExternalApiRequestAudits.AnyAsync(item =>
            item.ApiTokenId == allowed.TokenId &&
            item.ActorUserId == allowed.ActorUserId &&
            EF.Functions.Like(item.RouteKey, "%/audit/evidence/%")));
    }

    [Fact]
    public async Task RemoteSessions_UseRuntimeScopeAcrossDifferentTokenActors()
    {
        await using var host = CreateHost(new InMemoryAssetFileGateway());
        using var client = host.CreateClient();
        var fixture = await SeedAsync(host.Services);
        var creator = await IssueTokenAsync(
            host.Services,
            fixture.ScopeId,
            [ApiTokenScopes.TeamLabRemoteSessionsRead, ApiTokenScopes.TeamLabRemoteSessionsWrite]);
        var peer = await IssueTokenAsync(
            host.Services,
            fixture.ScopeId,
            [ApiTokenScopes.TeamLabRemoteSessionsRead, ApiTokenScopes.TeamLabRemoteSessionsWrite]);
        var foreign = await IssueTokenAsync(
            host.Services,
            fixture.ForeignScopeId,
            [ApiTokenScopes.TeamLabRemoteSessionsRead, ApiTokenScopes.TeamLabRemoteSessionsWrite]);

        Assert.NotEqual(fixture.OwnerUserId, creator.ActorUserId);
        Assert.NotEqual(creator.ActorUserId, peer.ActorUserId);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creator.PlainTextToken);
        using (var availability = await client.GetAsync(
                   $"/api/open/v1/teamlab/runtimes/{fixture.RuntimeId:D}/remote-access"))
            Assert.Equal(HttpStatusCode.OK, availability.StatusCode);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/open/v1/teamlab/runtimes/{fixture.RuntimeId:D}/assets/{fixture.AssetId}/remote-sessions")
        {
            Content = JsonContent.Create(new OpenCreateTeamLabRemoteSessionModel("cross-token operations", false))
        };
        createRequest.Headers.Add("Idempotency-Key", $"remote-create-{Guid.NewGuid():N}");
        using var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        var createOperation = await createResponse.Content.ReadFromJsonAsync<ApiOperationModel>(ApiJsonOptions);
        Assert.NotNull(createOperation);
        await WaitForOperationAsync(host.Services, createOperation.Id);

        var sessionId = createOperation.Id;
        await using (var verificationScope = host.Services.CreateAsyncScope())
        {
            var context = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await context.TeamLabRemoteSessions.AsNoTracking()
                .SingleAsync(item => item.PublicId == sessionId);
            Assert.Equal(creator.ActorUserId, session.RequestedByUserId);
            Assert.Equal(TeamLabRemoteSessionStatus.Ready, session.Status);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", peer.PlainTextToken);
        var sessionPath = $"/api/open/v1/teamlab/remote-sessions/{sessionId:D}";
        using (var get = await client.GetAsync(sessionPath))
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using (var connect = await client.PostAsync($"{sessionPath}/connect", null))
            await AssertProblemAsync(connect, HttpStatusCode.Conflict, "remote_session_terminal");
        using (var terminal = await client.GetAsync($"{sessionPath}/terminal"))
            Assert.Equal(HttpStatusCode.UpgradeRequired, terminal.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreign.PlainTextToken);
        using (var get = await client.GetAsync(sessionPath))
            await AssertProblemAsync(get, HttpStatusCode.NotFound, "scope_not_found");
        using (var terminal = await client.GetAsync($"{sessionPath}/terminal"))
            await AssertProblemAsync(terminal, HttpStatusCode.NotFound, "scope_not_found");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", peer.PlainTextToken);
        using var endRequest = new HttpRequestMessage(HttpMethod.Delete, sessionPath);
        endRequest.Headers.Add("Idempotency-Key", $"remote-end-{Guid.NewGuid():N}");
        using var endResponse = await client.SendAsync(endRequest);
        Assert.Equal(HttpStatusCode.Accepted, endResponse.StatusCode);
        var endOperation = await endResponse.Content.ReadFromJsonAsync<ApiOperationModel>(ApiJsonOptions);
        Assert.NotNull(endOperation);
        await WaitForOperationAsync(host.Services, endOperation.Id);

        await using var endedScope = host.Services.CreateAsyncScope();
        var endedContext = endedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(TeamLabRemoteSessionStatus.Ended,
            await endedContext.TeamLabRemoteSessions.AsNoTracking()
                .Where(item => item.PublicId == sessionId)
                .Select(item => item.Status)
                .SingleAsync());
    }

    [Fact]
    public async Task ServiceAccess_UsesRuntimeScopesForAutomaticAndManualMappings()
    {
        await using var host = CreateHost(new InMemoryAssetFileGateway());
        using var client = host.CreateClient();
        var fixture = await SeedAsync(host.Services);
        var allowed = await IssueTokenAsync(host.Services, fixture.ScopeId,
            [ApiTokenScopes.TeamLabRuntimesRead, ApiTokenScopes.TeamLabRuntimesWrite]);
        var readOnly = await IssueTokenAsync(host.Services, fixture.ScopeId,
            [ApiTokenScopes.TeamLabRuntimesRead]);
        var foreign = await IssueTokenAsync(host.Services, fixture.ForeignScopeId,
            [ApiTokenScopes.TeamLabRuntimesRead, ApiTokenScopes.TeamLabRuntimesWrite]);
        var runtimePath = $"/api/open/v1/teamlab/runtimes/{fixture.RuntimeId:D}";
        var createPath = $"{runtimePath}/assets/{fixture.AssetId}/service-access";

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readOnly.PlainTextToken);
        using (var denied = await client.PostAsJsonAsync(createPath,
                   new OpenCreateTeamLabServiceAccessModel("tcp", 8080)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", allowed.PlainTextToken);
        using var automaticResponse = await client.PostAsJsonAsync(createPath,
            new OpenCreateTeamLabServiceAccessModel("tcp", 8080));
        Assert.Equal(HttpStatusCode.Created, automaticResponse.StatusCode);
        var automatic = await automaticResponse.Content.ReadFromJsonAsync<OpenTeamLabServiceAccessModel>(ApiJsonOptions);
        Assert.NotNull(automatic);
        Assert.Equal(32010, automatic.PublicPort);
        Assert.Equal("gateway.example:32010", automatic.Endpoint);

        using var manualResponse = await client.PostAsJsonAsync(createPath,
            new OpenCreateTeamLabServiceAccessModel("udp", 502, 32123, "office"));
        Assert.Equal(HttpStatusCode.Created, manualResponse.StatusCode);
        var manual = await manualResponse.Content.ReadFromJsonAsync<OpenTeamLabServiceAccessModel>(ApiJsonOptions);
        Assert.NotNull(manual);
        Assert.Equal(32123, manual.PublicPort);

        var mappings = await client.GetFromJsonAsync<OpenTeamLabServiceAccessModel[]>(
            $"{runtimePath}/service-access", ApiJsonOptions);
        Assert.NotNull(mappings);
        Assert.Equal(2, mappings.Length);
        Assert.DoesNotContain("10.250.0.10", await automaticResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using (var revoked = await client.DeleteAsync($"{runtimePath}/service-access/{automatic.Id:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
            var result = await revoked.Content.ReadFromJsonAsync<OpenTeamLabServiceAccessModel>(ApiJsonOptions);
            Assert.Equal("revoked", result?.Status);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreign.PlainTextToken);
        using (var hidden = await client.GetAsync($"{runtimePath}/service-access"))
            await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "scope_not_found");
    }

    [Fact]
    public async Task AssetControl_UsesScopedTokenAcrossRuntimeOwnerAndReturnsOriginalTicket()
    {
        await using var host = CreateHost(new InMemoryAssetFileGateway());
        using var client = host.CreateClient();
        var fixture = await SeedAsync(host.Services);
        var allowed = await IssueTokenAsync(host.Services, fixture.ScopeId,
            [ApiTokenScopes.TeamLabRuntimesRead, ApiTokenScopes.TeamLabRuntimesWrite]);
        var foreign = await IssueTokenAsync(host.Services, fixture.ForeignScopeId,
            [ApiTokenScopes.TeamLabRuntimesRead, ApiTokenScopes.TeamLabRuntimesWrite]);
        Assert.NotEqual(fixture.OwnerUserId, allowed.ActorUserId);
        var controlPath = $"/api/open/v1/teamlab/runtimes/{fixture.RuntimeId:D}/assets/{fixture.AssetId}/control";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", allowed.PlainTextToken);

        var capability = await client.GetFromJsonAsync<OpenTeamLabAssetControlCapabilityModel>(controlPath, ApiJsonOptions);
        Assert.NotNull(capability);
        Assert.True(capability.Allowed, capability.Reason);

        var command = new OpenTeamLabAssetControlCommand(1, "stop", "external lifecycle control", true);
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, controlPath)
        {
            Content = JsonContent.Create(command)
        };
        firstRequest.Headers.Add("Idempotency-Key", "asset-control-http-001");
        using var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<OpenTeamLabAssetControlTicketModel>(ApiJsonOptions);
        Assert.NotNull(first);

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, controlPath)
        {
            Content = JsonContent.Create(command)
        };
        replayRequest.Headers.Add("Idempotency-Key", "asset-control-http-001");
        using var replayResponse = await client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Accepted, replayResponse.StatusCode);
        var replay = await replayResponse.Content.ReadFromJsonAsync<OpenTeamLabAssetControlTicketModel>(ApiJsonOptions);
        Assert.Equal(first.TicketId, replay?.TicketId);

        var taskPath = $"{controlPath}/{first.TicketId:D}";
        var task = await client.GetFromJsonAsync<OpenTeamLabAssetControlTaskModel>(taskPath, ApiJsonOptions);
        Assert.NotNull(task);
        Assert.Equal(first.TicketId, task.Id);

        using var conflictRequest = new HttpRequestMessage(HttpMethod.Post, controlPath)
        {
            Content = JsonContent.Create(command with { Action = "restart" })
        };
        conflictRequest.Headers.Add("Idempotency-Key", "asset-control-http-001");
        using (var conflict = await client.SendAsync(conflictRequest))
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "idempotency_conflict");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreign.PlainTextToken);
        using (var hidden = await client.GetAsync(taskPath))
            await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "scope_not_found");
        using (var noRetryEndpoint = await client.PostAsync($"{taskPath}/retry", null))
            Assert.Equal(HttpStatusCode.NotFound, noRetryEndpoint.StatusCode);
    }

    private WebApplicationFactory<Program> CreateHost(InMemoryAssetFileGateway gateway) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITeamLabAssetFileGateway>();
            services.AddSingleton<ITeamLabAssetFileGateway>(gateway);
            services.RemoveAll<ITeamLabRemoteRelayGateway>();
            services.AddSingleton<ITeamLabRemoteRelayGateway, NoOpRemoteRelayGateway>();
            services.RemoveAll<ITeamLabServiceAccessGateway>();
            services.AddSingleton<ITeamLabServiceAccessGateway, NoOpServiceAccessGateway>();
            services.RemoveAll<IPublicUdpGatewayProvider>();
            services.AddSingleton<IPublicUdpGatewayProvider, NoOpPublicGateway>();
            services.RemoveAll<IPortAllocationService>();
            services.AddSingleton<IPortAllocationService, InMemoryPortAllocationService>();
            services.RemoveAll<ITeamLabAssetControlGateway>();
            services.AddSingleton<ITeamLabAssetControlGateway, NoOpAssetControlGateway>();
            services.PostConfigure<PublicUdpGatewayConfig>(options => options.PublicEndpoint = "gateway.example");
        }));

    private static async Task WaitForOperationAsync(IServiceProvider services, Guid operationId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operation = await context.ApiOperations.AsNoTracking()
                .SingleAsync(item => item.Id == operationId);
            if (operation.Status == ApiOperationStatus.Succeeded)
                return;
            Assert.NotEqual(ApiOperationStatus.Failed, operation.Status);
            await Task.Delay(100);
        }

        Assert.Fail($"Operation {operationId:D} did not complete in time.");
    }

    private static async Task<Fixture> SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var owner = User($"owner-{suffix}", Role.Teacher);
        var controlScope = new TeamLabControlScope
        {
            Key = $"open-ops-{suffix}",
            DisplayName = "Open operations"
        };
        var foreignScope = new TeamLabControlScope
        {
            Key = $"foreign-{suffix}",
            DisplayName = "Foreign operations"
        };
        var node = new WorkerNode
        {
            Name = $"open-ops-{suffix}",
            HostAddress = "127.0.0.1",
            AuthToken = "integration-fixture",
            Status = NodeStatus.Online,
            TeamLabTunnelIp = "10.250.0.10"
        };
        var topology = new TeamLabTopology
        {
            ControlScope = controlScope,
            OwnerUserId = owner.Id,
            Name = $"open-ops-{suffix}",
            Revision = 1
        };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology,
            ControlScope = controlScope,
            Version = 1,
            SourceRevision = 1,
            CanonicalJson = "{}",
            ContentHash = new string('a', 64),
            PublishedById = owner.Id
        };
        var runtime = new TeamLabRuntime
        {
            ControlScope = controlScope,
            TopologyReleaseId = release.Id,
            CreatedById = owner.Id,
            Generation = 1,
            Status = TeamLabRuntimeStatus.Running,
            CreateRequestHash = $"open-ops-{suffix}"
        };
        var shard = new TeamLabRuntimeShard
        {
            Runtime = runtime,
            Generation = 1,
            WorkerNode = node,
            WorkerNodeId = node.Id,
            Status = TeamLabRuntimeStatus.Running
        };
        var asset = new TeamLabRuntimeAsset
        {
            Runtime = runtime,
            Shard = shard,
            Generation = 1,
            WorkerNode = node,
            Kind = TeamLabResourceKind.Docker,
            TopologyKey = "web",
            Name = "Web",
            RuntimeResourceId = $"container-{suffix}",
            NetworkKey = "office",
            IpAddress = "10.96.0.10",
            Status = TeamLabRuntimeStatus.Running
        };
        var session = new TeamLabRemoteSession
        {
            Runtime = runtime,
            RuntimeAsset = asset,
            WorkerNode = node,
            RequestedBy = owner,
            RequestedByUserId = owner.Id,
            Generation = 1,
            Protocol = TeamLabRemoteProtocol.ContainerTerminal,
            Status = TeamLabRemoteSessionStatus.Ended,
            Reason = "integration evidence",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            EndedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            EndReason = "completed"
        };

        context.Users.Add(owner);
        context.TeamLabControlScopes.AddRange(controlScope, foreignScope);
        context.WorkerNodes.Add(node);
        context.TeamLabTopologies.Add(topology);
        context.TeamLabTopologyReleases.Add(release);
        context.TeamLabRuntimes.Add(runtime);
        context.TeamLabRuntimeShards.Add(shard);
        context.TeamLabRuntimeAssets.Add(asset);
        context.TeamLabRemoteSessions.Add(session);
        await context.SaveChangesAsync();
        var plan = new TeamLabExecutionPlanV2(
            runtime.Id, runtime.PublicId, 1, "shard", string.Empty,
            "sha256:" + new string('a', 64), false, [],
            [new("web", "docker", "web", "sha256:" + new string('a', 64), null, 1, 1, 128, [], [])], []);
        plan = plan with
        {
            PlanDigest = "sha256:" + Convert.ToHexStringLower(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan)))
        };
        Assert.True(plan.IsValid(out var planError), planError);
        context.TeamLabExecutionPlanSnapshots.Add(new TeamLabExecutionPlanSnapshot
        {
            Runtime = runtime,
            Shard = shard,
            Generation = 1,
            WorkerNodeId = node.Id,
            PlanDigest = plan.PlanDigest,
            PlanJson = JsonSerializer.Serialize(plan)
        });
        await context.SaveChangesAsync();
        return new Fixture(
            controlScope.Id,
            foreignScope.Id,
            runtime.PublicId,
            asset.Id,
            session.PublicId,
            owner.Id);
    }

    private static async Task<IssuedToken> IssueTokenAsync(
        IServiceProvider services,
        Guid controlScopeId,
        IReadOnlyCollection<string> scopes)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = scope.ServiceProvider.GetRequiredService<ApiTokenIssuer>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var actor = User($"api-{suffix}", Role.Admin);
        context.Users.Add(actor);
        await context.SaveChangesAsync();
        var issued = await issuer.IssueAsync(
            new ActorContext(actor.Id, actor.Role),
            new IssueApiTokenCommand(
                "TeamLab open operations",
                scopes,
                [new ApiTokenResourceGrantSpec("teamlab-scope", controlScopeId.ToString("D"))],
                120,
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);
        return new IssuedToken(issued.PlainTextToken, issued.Token.Id, actor.Id);
    }

    private static UserInfo User(string name, Role role) => new()
    {
        Id = Guid.CreateVersion7(),
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        EmailConfirmed = true,
        Role = role,
        RegisterTimeUtc = DateTimeOffset.UtcNow
    };

    private static JsonSerializerOptions CreateApiJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new DateTimeOffsetJsonConverter());
        return options;
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.NotNull(problem);
        Assert.Equal(code, problem["code"].GetString());
    }

    private sealed record Fixture(
        Guid ScopeId,
        Guid ForeignScopeId,
        Guid RuntimeId,
        int AssetId,
        Guid SessionId,
        Guid OwnerUserId);

    private sealed record IssuedToken(string PlainTextToken, Guid TokenId, Guid ActorUserId);

    private sealed class NoOpRemoteRelayGateway : ITeamLabRemoteRelayGateway
    {
        public Task<IReadOnlyList<Guid>> InventoryAsync(Guid workerNodeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<TeamLabRemoteRelayResult> CreateAsync(
            Guid workerNodeId, TeamLabRemoteRelayRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TeamLabRemoteRelayResult(22000, request.ExpiresAt));

        public Task DeleteAsync(Guid workerNodeId, Guid sessionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CancelTerminalAsync(Guid workerNodeId, Guid sessionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ProxyTerminalAsync(
            Guid workerNodeId,
            Guid sessionId,
            int runtimeId,
            int generation,
            string runtimeResourceId,
            DateTimeOffset expiresAt,
            System.Net.WebSockets.WebSocket socket,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoOpServiceAccessGateway : ITeamLabServiceAccessGateway
    {
        public Task ApplyAsync(Guid nodeId, TeamLabServiceForwardRequest request, CancellationToken token) =>
            Task.CompletedTask;

        public Task RemoveAsync(Guid nodeId, TeamLabServiceForwardRequest request, CancellationToken token) =>
            Task.CompletedTask;
    }

    private sealed class NoOpPublicGateway : IPublicUdpGatewayProvider
    {
        public Task<PublicUdpGatewaySyncResult> SyncMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token) =>
            Task.FromResult(new PublicUdpGatewaySyncResult(true, "ok", []));

        public Task<PublicUdpGatewaySyncResult> RemoveMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token) =>
            Task.FromResult(new PublicUdpGatewaySyncResult(true, "ok", []));

        public Task<PublicUdpGatewaySyncResult> SyncServiceAsync(PublicServiceGatewayMapping mapping, CancellationToken token) =>
            Task.FromResult(new PublicUdpGatewaySyncResult(true, "ok", []));

        public Task<PublicUdpGatewaySyncResult> RemoveServiceAsync(PublicServiceGatewayMapping mapping, CancellationToken token) =>
            Task.FromResult(new PublicUdpGatewaySyncResult(true, "ok", []));
    }

    private sealed class InMemoryPortAllocationService : IPortAllocationService
    {
        private int next = 32010;
        public bool IsRedisBacked => false;
        public PortAllocationRange CurrentRange => new(32010, 32999, "test", false);

        public Task<PortLease?> AllocatePortAsync(Guid containerId, CancellationToken token = default) =>
            Task.FromResult<PortLease?>(new(next++, containerId, DateTimeOffset.UtcNow.AddMinutes(5)));

        public Task<bool> ReleasePortAsync(int port, Guid leaseId, CancellationToken token = default) =>
            Task.FromResult(true);

        public Task<bool> ReserveExistingPortAsync(int port, Guid leaseId, CancellationToken token = default) =>
            Task.FromResult(true);
    }

    private sealed class NoOpAssetControlGateway : ITeamLabAssetControlGateway
    {
        public Task<TeamLabAssetControlResult> ExecuteAsync(
            Guid nodeId,
            TeamLabAssetControlRequest request,
            CancellationToken token)
        {
            var state = request.Action switch
            {
                "stop" => "exited",
                "pause" => "paused",
                "remove" => null,
                _ => "running"
            };
            var fact = state is null ? null : new TeamLabExecutionInventoryFactV2(
                "docker", request.AssetKey, request.ExpectedResourceId ?? "recreated", state, request.Plan.Generation);
            return Task.FromResult(new TeamLabAssetControlResult(true, null, fact));
        }
    }

    private sealed class InMemoryAssetFileGateway : ITeamLabAssetFileGateway
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        public ConcurrentQueue<string> Operations { get; } = new();

        public void SetFile(string path, byte[] content) => files[path] = content;

        public Task<TeamLabFileResult> ExecuteAsync(
            Guid nodeId,
            TeamLabContainerFileRequest request,
            CancellationToken token)
        {
            Operations.Enqueue(request.Operation);
            return Task.FromResult(request.Operation switch
            {
                "list" => new TeamLabFileResult(Entries: files
                    .Select(item => new TeamLabFileEntry(
                        item.Key.TrimStart('/'),
                        "file",
                        item.Value.Length))
                    .ToArray()),
                "download" => new TeamLabFileResult(Content: files[request.Path]),
                "upload" => Store(request.Path, request.Content ?? []),
                "mkdir" => new TeamLabFileResult(),
                "move" => Move(request.Path, request.DestinationPath!),
                "delete" => Delete(request.Path),
                _ => throw new InvalidOperationException($"Unexpected operation: {request.Operation}")
            });
        }

        public Task<TeamLabFileResult> ExecuteVmAsync(
            Guid nodeId,
            TeamLabVmFileRequest request,
            CancellationToken token) =>
            throw new NotSupportedException("The HTTP fixture uses a container asset.");

        private TeamLabFileResult Store(string path, byte[] content)
        {
            files[path] = content;
            return new TeamLabFileResult();
        }

        private TeamLabFileResult Move(string source, string destination)
        {
            files[destination] = files[source];
            files.Remove(source);
            return new TeamLabFileResult();
        }

        private TeamLabFileResult Delete(string path)
        {
            files.Remove(path);
            return new TeamLabFileResult();
        }
    }
}
