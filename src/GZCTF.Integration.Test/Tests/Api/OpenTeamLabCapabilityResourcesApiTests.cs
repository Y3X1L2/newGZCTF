using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts;
using GZCTF.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class OpenTeamLabCapabilityResourcesApiTests(GZCTFApplicationFactory factory)
{
    private static readonly JsonSerializerOptions ApiJsonOptions = CreateApiJsonOptions();

    [Fact]
    public async Task DevicePackages_RequireWildcardAndCompleteLifecycle()
    {
        var scopeId = await SeedScopeAsync(factory.Services, "device-packages");
        var wildcard = await IssueTokenAsync(
            factory.Services,
            [ApiTokenScopes.TeamLabDevicePackagesRead, ApiTokenScopes.TeamLabDevicePackagesWrite],
            "*");
        var scoped = await IssueTokenAsync(
            factory.Services,
            [ApiTokenScopes.TeamLabDevicePackagesRead, ApiTokenScopes.TeamLabDevicePackagesWrite],
            scopeId.ToString("D"));
        var readOnly = await IssueTokenAsync(
            factory.Services,
            [ApiTokenScopes.TeamLabDevicePackagesRead],
            "*");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = DevicePackage($"open-plc-{suffix}", "1.0.0", "PLC 模板");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(scoped);
        using (var denied = await client.PostAsJsonAsync("/api/open/v1/teamlab/device-packages", create))
            await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "insufficient_permission");

        client.DefaultRequestHeaders.Authorization = Bearer(readOnly);
        using (var denied = await client.PostAsJsonAsync("/api/open/v1/teamlab/device-packages", create))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(wildcard);
        using var createdResponse = await client.PostAsJsonAsync("/api/open/v1/teamlab/device-packages", create);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<TeamLabDevicePackageModel>(ApiJsonOptions);
        Assert.NotNull(created);
        Assert.Equal(create.DisplayName, created.DisplayName);

        var update = new OpenUpdateTeamLabDevicePackageModel(
            create.Name, "PLC 模板（更新）", "1.0.1", create.ArtifactKind,
            "registry.example.test/plc:1.0.1", "sha256:" + new string('b', 64), "更新后的模板",
            create.SupportedAssetKinds, 750, 384, 6, create.Ports,
            create.ParameterSchema, create.HealthDeclaration, ["modbus-read", "modbus-write"]);
        using var updatedResponse = await client.PutAsJsonAsync(
            $"/api/open/v1/teamlab/device-packages/{created.Id:D}", update);
        updatedResponse.EnsureSuccessStatusCode();
        var updated = await updatedResponse.Content.ReadFromJsonAsync<TeamLabDevicePackageModel>(ApiJsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("PLC 模板（更新）", updated.DisplayName);
        Assert.Equal("1.0.1", updated.Version);
        Assert.Equal(750, updated.CpuMillis);

        var disabled = await client.PostAsync(
            $"/api/open/v1/teamlab/device-packages/{created.Id:D}/disable", null);
        disabled.EnsureSuccessStatusCode();
        Assert.False((await disabled.Content.ReadFromJsonAsync<TeamLabDevicePackageModel>(ApiJsonOptions))!.Enabled);

        var enabled = await client.PostAsync(
            $"/api/open/v1/teamlab/device-packages/{created.Id:D}/enable", null);
        enabled.EnsureSuccessStatusCode();
        Assert.True((await enabled.Content.ReadFromJsonAsync<TeamLabDevicePackageModel>(ApiJsonOptions))!.Enabled);

        using (var archive = await client.PostAsync(
                   $"/api/open/v1/teamlab/device-packages/{created.Id:D}/archive", null))
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        using (var missing = await client.GetAsync(
                   $"/api/open/v1/teamlab/device-packages/{created.Id:D}"))
            await AssertProblemAsync(missing, HttpStatusCode.NotFound, "device_package_not_found");
    }

    [Fact]
    public async Task Connectors_DiscoverUpdateProbeAndArchiveWithinGrantedScope()
    {
        var gateway = new TestConnectorNodeGateway();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITeamLabConnectorNodeGateway>();
            services.AddSingleton<ITeamLabConnectorNodeGateway>(gateway);
        }));
        var fixture = await SeedConnectorFixtureAsync(host.Services);
        gateway.Interfaces[fixture.NodeId] =
            [new TeamLabHostInterface("enp2s0", "02:00:00:00:00:01", true, ["10.20.0.2/24"])];
        var allowed = await IssueTokenAsync(
            host.Services,
            [ApiTokenScopes.TeamLabConnectorsRead, ApiTokenScopes.TeamLabConnectorsWrite],
            fixture.ScopeId.ToString("D"));
        var foreign = await IssueTokenAsync(
            host.Services,
            [ApiTokenScopes.TeamLabConnectorsRead, ApiTokenScopes.TeamLabConnectorsWrite],
            fixture.ForeignScopeId.ToString("D"));
        var wildcard = await IssueTokenAsync(
            host.Services,
            [ApiTokenScopes.TeamLabConnectorsRead, ApiTokenScopes.TeamLabConnectorsWrite],
            "*");
        using var client = host.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(foreign);
        using (var denied = await client.GetAsync(
                   $"/api/open/v1/teamlab/connectors/nodes/{fixture.NodeId:D}/interfaces"))
            await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "insufficient_permission");

        client.DefaultRequestHeaders.Authorization = Bearer(wildcard);
        var interfaces = await client.GetFromJsonAsync<IReadOnlyList<TeamLabHostInterface>>(
            $"/api/open/v1/teamlab/connectors/nodes/{fixture.NodeId:D}/interfaces");
        Assert.Equal("enp2s0", Assert.Single(interfaces!).Name);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = new OpenRegisterTeamLabConnectorModel(
            $"field-nic-{suffix}", "现场网卡", "managed-nic", fixture.ScopeId, false, 1,
            null, "初始连接器", new TeamLabManagedNicModel(fixture.NodeId, "enp2s0", "02:00:00:00:00:01"));
        client.DefaultRequestHeaders.Authorization = Bearer(allowed);
        using var createdResponse = await client.PostAsJsonAsync("/api/open/v1/teamlab/connectors", create);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<TeamLabConnectorModel>(ApiJsonOptions);
        Assert.NotNull(created);

        client.DefaultRequestHeaders.Authorization = Bearer(foreign);
        using (var hidden = await client.GetAsync(
                   $"/api/open/v1/teamlab/connectors/{created.Id:D}?scopeId={fixture.ForeignScopeId:D}"))
            await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "connector_not_found");
        using (var hidden = await client.PutAsJsonAsync(
                   $"/api/open/v1/teamlab/connectors/{created.Id:D}",
                   new OpenUpdateTeamLabConnectorModel(create.Name, "越权更新", create.Kind,
                       fixture.ForeignScopeId, false, 1, null, null, create.ManagedNic)))
            await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "scope_not_found");

        client.DefaultRequestHeaders.Authorization = Bearer(allowed);
        var update = new OpenUpdateTeamLabConnectorModel(
            create.Name, "现场网卡（更新）", create.Kind, fixture.ScopeId, false, 1,
            null, "更新后的连接器", create.ManagedNic);
        using var updatedResponse = await client.PutAsJsonAsync(
            $"/api/open/v1/teamlab/connectors/{created.Id:D}", update);
        updatedResponse.EnsureSuccessStatusCode();
        var updated = await updatedResponse.Content.ReadFromJsonAsync<TeamLabConnectorModel>(ApiJsonOptions);
        Assert.Equal("现场网卡（更新）", updated!.DisplayName);

        var health = await client.GetFromJsonAsync<OpenTeamLabConnectorHealthModel>(
            $"/api/open/v1/teamlab/connectors/{created.Id:D}/health", ApiJsonOptions);
        Assert.NotNull(health);
        Assert.Equal("healthy", health.Health);
        Assert.NotNull(health.ObservedAt);

        using (var archive = await client.PostAsync(
                   $"/api/open/v1/teamlab/connectors/{created.Id:D}/archive", null))
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        using (var missing = await client.GetAsync(
                   $"/api/open/v1/teamlab/connectors/{created.Id:D}?scopeId={fixture.ScopeId:D}"))
            await AssertProblemAsync(missing, HttpStatusCode.NotFound, "connector_not_found");
    }

    private static OpenRegisterTeamLabDevicePackageModel DevicePackage(string name, string version, string displayName) =>
        new(name, displayName, version, "oci-image", $"registry.example.test/{name}:{version}",
            "sha256:" + new string('a', 64), null, ["docker"], 500, 256, 4,
            [new TeamLabDevicePackagePortModel("modbus", 502, "tcp")],
            Json("""{"type":"object"}"""), Json("""{"kind":"tcp","port":502}"""), ["modbus-read"]);

    private static async Task<Guid> SeedScopeAsync(IServiceProvider services, string prefix)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var controlScope = new TeamLabControlScope { Key = $"{prefix}-{suffix}", DisplayName = prefix };
        context.TeamLabControlScopes.Add(controlScope);
        await context.SaveChangesAsync();
        return controlScope.Id;
    }

    private static async Task<ConnectorFixture> SeedConnectorFixtureAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var controlScope = new TeamLabControlScope { Key = $"connector-a-{suffix}", DisplayName = "Connector A" };
        var foreignScope = new TeamLabControlScope { Key = $"connector-b-{suffix}", DisplayName = "Connector B" };
        var node = new WorkerNode { Name = $"field-{suffix}", HostAddress = "10.0.0.10" };
        context.TeamLabControlScopes.AddRange(controlScope, foreignScope);
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        return new ConnectorFixture(controlScope.Id, foreignScope.Id, node.Id);
    }

    private static async Task<string> IssueTokenAsync(
        IServiceProvider services,
        IReadOnlyCollection<string> scopes,
        string resourceId)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = scope.ServiceProvider.GetRequiredService<ApiTokenIssuer>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"cap-{suffix}";
        var actor = new UserInfo
        {
            Id = Guid.CreateVersion7(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"capability-{suffix}@example.test",
            NormalizedEmail = $"CAPABILITY-{suffix}@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Admin,
            RegisterTimeUtc = DateTimeOffset.UtcNow
        };
        context.Users.Add(actor);
        await context.SaveChangesAsync();
        var issued = await issuer.IssueAsync(
            new ActorContext(actor.Id, actor.Role),
            new IssueApiTokenCommand(
                "TeamLab capability resources", scopes,
                [new ApiTokenResourceGrantSpec("teamlab-scope", resourceId)],
                120, DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);
        return issued.PlainTextToken;
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

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
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status,
            $"Expected {(int)status}, received {(int)response.StatusCode}: {content}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(content);
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record ConnectorFixture(Guid ScopeId, Guid ForeignScopeId, Guid NodeId);

    private sealed class TestConnectorNodeGateway : ITeamLabConnectorNodeGateway
    {
        public Dictionary<Guid, IReadOnlyList<TeamLabHostInterface>> Interfaces { get; } = [];

        public Task<IReadOnlyList<TeamLabHostInterface>> GetInterfacesAsync(
            Guid nodeId,
            CancellationToken token) =>
            Task.FromResult(Interfaces.GetValueOrDefault(nodeId, []));
    }
}
