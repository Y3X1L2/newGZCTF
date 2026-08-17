using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabLinkPolicyTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"link-policies-{Guid.NewGuid():N}")
            .Options);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static async Task<TeamLabRuntime> AddRuntimeAsync(AppDbContext context)
    {
        var runtime = new TeamLabRuntime { Status = TeamLabRuntimeStatus.Running };
        runtime.Networks.Add(new TeamLabRuntimeNetwork { TopologyKey = "office-net", Name = "office" });
        runtime.Assets.Add(new TeamLabRuntimeAsset { TopologyKey = "plc-1", Name = "plc-1" });
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        return runtime;
    }

    private static ApplyTeamLabLinkPolicyModel Command(
        Guid runtimeId,
        string kind,
        string parameters,
        string? assetKey = null,
        DateTimeOffset? recoverAt = null) => new(
        runtimeId, "office-net", assetKey, kind, Json(parameters), recoverAt);

    [Fact]
    public async Task Apply_StoresCanonicalParameters_AndReapplyIsIdempotent()
    {
        using var context = CreateContext();
        var service = new TeamLabLinkPolicyService(context);
        var runtime = await AddRuntimeAsync(context);

        var policy = await service.ApplyAsync(
            Command(runtime.PublicId, "latency", """{"delayMillis": 120}"""), CancellationToken.None);
        Assert.Equal("active", policy.Status);
        Assert.Equal(120, policy.Parameters!.Value.GetProperty("delayMillis").GetInt32());

        var again = await service.ApplyAsync(
            Command(runtime.PublicId, "latency", """{"delayMillis":120}"""), CancellationToken.None);
        Assert.Equal(policy.Id, again.Id);
    }

    [Fact]
    public async Task Apply_ConflictingParametersRequireRecoverFirst()
    {
        using var context = CreateContext();
        var service = new TeamLabLinkPolicyService(context);
        var runtime = await AddRuntimeAsync(context);
        await service.ApplyAsync(
            Command(runtime.PublicId, "latency", """{"delayMillis":120}"""), CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.ApplyAsync(
            Command(runtime.PublicId, "latency", """{"delayMillis":250}"""), CancellationToken.None));
        Assert.Equal("link_policy_conflict", conflict.Code);

        var recovered = await service.RecoverAsync(
            (await service.ListByRuntimeAsync(runtime.PublicId, "active", null, 50, CancellationToken.None))
            .Items[0].Id, CancellationToken.None);
        Assert.Equal("recovered", recovered.Status);

        var reapplied = await service.ApplyAsync(
            Command(runtime.PublicId, "latency", """{"delayMillis":250}"""), CancellationToken.None);
        Assert.Equal(250, reapplied.Parameters!.Value.GetProperty("delayMillis").GetInt32());
    }

    [Theory]
    [InlineData("latency", """{"delayMillis":0}""")]
    [InlineData("packet-loss", """{"lossPercent":101}""")]
    [InlineData("latency", """{}""")]
    [InlineData("access-rule", """{"direction":"sideways","action":"deny"}""")]
    [InlineData("nat", """{"mode":"dnat","externalPort":80}""")]
    [InlineData("link-break", "[]")]
    public async Task Apply_RejectsInvalidKindParameters(string kind, string parameters)
    {
        using var context = CreateContext();
        var service = new TeamLabLinkPolicyService(context);
        var runtime = await AddRuntimeAsync(context);

        var exception = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.ApplyAsync(Command(runtime.PublicId, kind, parameters), CancellationToken.None));
        Assert.Equal("link_policy_parameters_invalid", exception.Code);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task Apply_ValidAccessRuleAndNat_AcceptOptionalFields()
    {
        using var context = CreateContext();
        var service = new TeamLabLinkPolicyService(context);
        var runtime = await AddRuntimeAsync(context);

        var access = await service.ApplyAsync(Command(runtime.PublicId, "access-rule",
            """{"direction":"inbound","action":"deny","protocol":"tcp","sourceCidr":"10.0.0.0/8","priority":100}""",
            assetKey: "plc-1"), CancellationToken.None);
        Assert.Equal("plc-1", access.AssetKey);
        Assert.Equal("tcp", access.Parameters!.Value.GetProperty("protocol").GetString());

        var nat = await service.ApplyAsync(Command(runtime.PublicId, "nat",
            """{"mode":"snat","translatedAddress":"172.16.0.9"}"""), CancellationToken.None);
        Assert.Equal("snat", nat.Parameters!.Value.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Apply_RejectsUnknownNetworkAndAsset()
    {
        using var context = CreateContext();
        var service = new TeamLabLinkPolicyService(context);
        var runtime = await AddRuntimeAsync(context);

        var network = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.ApplyAsync(
            new ApplyTeamLabLinkPolicyModel(runtime.PublicId, "ghost-net", null, "latency", Json("""{"delayMillis":10}"""), null),
            CancellationToken.None));
        Assert.Equal("link_policy_network_unknown", network.Code);

        var asset = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.ApplyAsync(
            Command(runtime.PublicId, "latency", """{"delayMillis":10}""", assetKey: "ghost-asset"), CancellationToken.None));
        Assert.Equal("link_policy_asset_unknown", asset.Code);
    }

    [Fact]
    public async Task Apply_RejectsPastRecoverAt_AndTerminatedRuntime()
    {
        using var context = CreateContext();
        var service = new TeamLabLinkPolicyService(context);
        var runtime = await AddRuntimeAsync(context);

        var past = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.ApplyAsync(
            Command(runtime.PublicId, "latency", """{"delayMillis":10}""", recoverAt: DateTimeOffset.UtcNow.AddMinutes(-1)),
            CancellationToken.None));
        Assert.Equal("link_policy_recover_at_invalid", past.Code);

        runtime.Status = TeamLabRuntimeStatus.Destroyed;
        await context.SaveChangesAsync();
        var terminated = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.ApplyAsync(
            Command(runtime.PublicId, "latency", """{"delayMillis":10}"""), CancellationToken.None));
        Assert.Equal("runtime_not_active", terminated.Code);
    }

    [Fact]
    public async Task List_FiltersByStatus_AndValidatesTheFilter()
    {
        using var context = CreateContext();
        var service = new TeamLabLinkPolicyService(context);
        var runtime = await AddRuntimeAsync(context);
        await service.ApplyAsync(Command(runtime.PublicId, "latency", """{"delayMillis":10}"""), CancellationToken.None);
        await service.ApplyAsync(Command(runtime.PublicId, "jitter", """{"jitterMillis":5}"""), CancellationToken.None);
        var page = await service.ListByRuntimeAsync(runtime.PublicId, null, null, 50, CancellationToken.None);
        await service.RecoverAsync(page.Items[0].Id, CancellationToken.None);

        var active = await service.ListByRuntimeAsync(runtime.PublicId, "active", null, 50, CancellationToken.None);
        Assert.Single(active.Items);

        var recovered = await service.ListByRuntimeAsync(runtime.PublicId, "recovered", null, 50, CancellationToken.None);
        Assert.Single(recovered.Items);
        Assert.Equal("manual", recovered.Items[0].RecoverOrigin);

        var invalid = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.ListByRuntimeAsync(runtime.PublicId, "bogus", null, 50, CancellationToken.None));
        Assert.Equal("link_policy_status_invalid", invalid.Code);
    }
}
