using System;
using System.Linq;
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

public sealed class TeamLabCapabilityBindingTests
{
    [Fact]
    public void ReleaseCodec_V2RoundTripsDevicePackageAndConnectorBinding()
    {
        var definition = MinimalDefinition();
        var digests = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["plc-1"] = "sha256:" + new string('b', 64)
        };

        var canonical = TeamLabReleaseCodec.Encode(2, definition, null, digests);
        var decoded = TeamLabReleaseCodec.DecodeDefinition(2, canonical);
        var execution = TeamLabReleaseCodec.DecodeExecution(2, canonical);

        var asset = Assert.Single(decoded.Assets, item => item.Key == "plc-1");
        Assert.Equal(7, asset.DevicePackageId);
        Assert.Equal("10.0.0.9", asset.DeviceParameters!.Value.GetProperty("gateway").GetString());
        Assert.Equal(Guid.Parse("01900000-0000-7000-8000-0000000000c1"), asset.ConnectorId);
        var executionAsset = Assert.Single(execution.Assets, item => item.Key == "plc-1");
        Assert.Equal(digests["plc-1"], executionAsset.DevicePackageDigest);
        Assert.Equal(asset.DevicePackageId, executionAsset.DevicePackageId);
        Assert.Equal(asset.ConnectorId, executionAsset.ConnectorId);
    }

    [Fact]
    public void ReleaseCodec_V2CanonicalizesEquivalentDeviceParameters()
    {
        var spaced = MinimalDefinition() with
        {
            Assets = [Asset("plc-1", "entry",
                deviceParameters: JsonDocument.Parse("{ \"gateway\" : \"10.0.0.9\" }").RootElement.Clone())]
        };

        var first = TeamLabReleaseCodec.Encode(2, MinimalDefinition());
        var second = TeamLabReleaseCodec.Encode(2, spaced);

        Assert.Equal(first, second);
    }

    [Fact]
    public void StructureValidator_RejectsParametersWithoutPackage()
    {
        var definition = MinimalDefinition() with
        {
            Assets =
            [
                Asset("plc-1", "entry", packageId: 0)
            ]
        };

        var result = new TeamLabTopologyValidator().Validate(definition, 2);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Code == "device_parameters_without_package");
    }

    [Fact]
    public async Task CapabilityResourceValidation_RejectsMissingOrDisabledReferences()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"capability-binding-{Guid.NewGuid():N}")
            .Options);
        context.TeamLabDevicePackages.Add(new TeamLabDevicePackage
        {
            Id = 7,
            Name = "plc-simulator",
            DisplayName = "PLC 模拟器",
            Version = "1.0.0",
            SupportedAssetKindsJson = """["vm"]"""
        });
        context.TeamLabConnectors.Add(new TeamLabConnector
        {
            PublicId = Guid.Parse("01900000-0000-7000-8000-0000000000c1"),
            Name = "field-vlan",
            DisplayName = "现场 VLAN"
        });
        await context.SaveChangesAsync();

        var missing = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            TeamLabTopologyApplicationService.ValidateCapabilityResourcesAsync(
                context, MinimalDefinition(packageId: 404), CancellationToken.None));
        Assert.Equal("device_package_unavailable", missing.Code);

        var mismatch = MinimalDefinition();
        mismatch = mismatch with
        {
            Assets = [Asset("plc-1", "entry", packageId: 7, kind: TeamLabAssetKind.Docker)]
        };
        var kind = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            TeamLabTopologyApplicationService.ValidateCapabilityResourcesAsync(context, mismatch, CancellationToken.None));
        Assert.Equal("device_package_unavailable", kind.Code);

        var unknownConnector = MinimalDefinition() with
        {
            Assets = [Asset("plc-1", "entry", connectorId: Guid.NewGuid())]
        };
        var connector = await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            TeamLabTopologyApplicationService.ValidateCapabilityResourcesAsync(
                context, unknownConnector, CancellationToken.None));
        Assert.Equal("connector_unavailable", connector.Code);

        await TeamLabTopologyApplicationService.ValidateCapabilityResourcesAsync(
            context, MinimalDefinition(), CancellationToken.None);
    }

    [Fact]
    public async Task FinalizeGeneration_ReleasesConnectorLeasesAndPoliciesOnDestroyOnly()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"finalize-binding-{Guid.NewGuid():N}")
            .Options);
        var runtime = await SeedRuntimeWithCapabilityResourcesAsync(context);
        var lease = context.TeamLabConnectorLeases.AsQueryable().Single();
        var policy = context.TeamLabLinkPolicies.AsQueryable().Single();

        await TeamLabRuntimeCleanupService.FinalizeGenerationAsync(
            context, runtime, runtime.Generation, markRuntimeDestroyed: false, CancellationToken.None);
        Assert.Null(lease.ReleasedAt);
        Assert.Equal(TeamLabLinkPolicyStatus.Active, policy.Status);

        await TeamLabRuntimeCleanupService.FinalizeGenerationAsync(
            context, runtime, runtime.Generation + 1, markRuntimeDestroyed: true, CancellationToken.None);
        Assert.NotNull(lease.ReleasedAt);
        Assert.Equal(TeamLabConnectorLeaseReleaseReason.RuntimeDestroyed, lease.ReleaseReason);
        Assert.Equal(TeamLabLinkPolicyStatus.Recovered, policy.Status);
        Assert.Equal(TeamLabLinkPolicyRecoverOrigin.RuntimeDestroyed, policy.RecoverOrigin);
    }

    private static async Task<TeamLabRuntime> SeedRuntimeWithCapabilityResourcesAsync(AppDbContext context)
    {
        var connector = new TeamLabConnector { Name = "field-vlan", DisplayName = "现场 VLAN" };
        var runtime = new TeamLabRuntime
        {
            Status = TeamLabRuntimeStatus.Destroying,
            Networks = [new TeamLabRuntimeNetwork { TopologyKey = "entry", Name = "entry" }],
            Assets = [new TeamLabRuntimeAsset { TopologyKey = "plc-1", Name = "plc-1" }],
            Shards = [new TeamLabRuntimeShard { WorkerNodeId = Guid.NewGuid() }]
        };
        context.TeamLabConnectors.Add(connector);
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        context.TeamLabConnectorLeases.Add(new TeamLabConnectorLease
        {
            ConnectorId = connector.Id,
            RuntimeId = runtime.Id,
            Slot = 1
        });
        context.TeamLabLinkPolicies.Add(new TeamLabLinkPolicy
        {
            RuntimeId = runtime.Id,
            NetworkKey = "entry",
            Kind = TeamLabLinkPolicyKind.Latency,
            ParametersJson = """{"delayMillis":50}""",
            Status = TeamLabLinkPolicyStatus.Active
        });
        await context.SaveChangesAsync();
        return runtime;
    }

    private static TeamLabTopologyDefinitionModel MinimalDefinition(int packageId = 7) => new(
        "field-scenario",
        [Network("entry", "10.48.0.0/16", true)],
        [Asset("plc-1", "entry", packageId)],
        []);

    internal static TeamLabTopologyAssetModel Asset(
        string key,
        string networkKey,
        int packageId = 7,
        TeamLabAssetKind kind = TeamLabAssetKind.Vm,
        Guid? connectorId = null,
        JsonElement? deviceParameters = null) => new(
        key,
        key,
        kind,
        1,
        new TeamLabAssetResourceModel(10, 512, 512),
        [new TeamLabTopologyInterfaceModel("eth0", networkKey, 10, true)],
        DevicePackageId: packageId,
        DeviceParameters: deviceParameters ??
                          JsonDocument.Parse("""{"gateway":"10.0.0.9"}""").RootElement.Clone(),
        ConnectorId: connectorId ?? Guid.Parse("01900000-0000-7000-8000-0000000000c1"));

    internal static TeamLabTopologyNetworkModel Network(string key, string cidr, bool isEntry) => new(
        key, key, new TeamLabAddressPoolModel(cidr, 24), isEntry);
}
