using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.TeamLab.Contracts.Execution;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabManagedGuestNetworkTests
{
    [Fact]
    public async Task AssetChanges_RejectOldAgentAndAcceptAdvertisedWindowsSupport()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var node = new WorkerNode { Name = "old-agent" };
        context.WorkerNodes.Add(node);
        context.ImageTemplates.Add(new ImageTemplate { Id = 1, Name = "windows", OSType = OSType.Windows, VmNetworkMode = VmNetworkMode.ManagedStatic });
        await context.SaveChangesAsync();
        var runtime = new TeamLabRuntime { Generation = 1, Networks = [
            new() { TopologyKey = "entry", WorkerNodeId = node.Id }, new() { TopologyKey = "core", WorkerNodeId = node.Id }] };
        var service = new TeamLabRuntimeUpdateService(context, payloads: null!, topologyValidator: null!, queue: null!, capacity: null!,
            lifecycleGuard: null!, deployment: null!, nodes: null!, assetControl: null!, artifacts: null!, remoteAccess: null!,
            serviceAccess: null!, traffic: null!, events: null!, logger: NullLogger<TeamLabRuntimeUpdateService>.Instance);
        var target = TeamLabTopologyV2Compiler.Compile(Definition());
        var reason = await service.GuestNetworkCapabilityReasonAsync(runtime, target, CancellationToken.None);
        Assert.Contains(AgentFeatureIds.TeamLabManagedGuestNetwork, reason!);
        node.CapabilityManifestJson = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest("new", null, 1,
            [AgentFeatureIds.TeamLabManagedGuestNetwork], new(1, 1, 1, 1), new(4, 4L * 1024 * 1024 * 1024), DateTimeOffset.UtcNow)).Json;
        await context.SaveChangesAsync();
        Assert.Null(await service.GuestNetworkCapabilityReasonAsync(runtime, target, CancellationToken.None));
    }

    [Fact]
    public void DeploymentPolicy_UsesResolvedModeAndTemplateOs_WithoutGatingLegacyModes()
    {
        var declared = TeamLabTopologyV2Compiler.Compile(Definition()).Assets[0];
        var runtime = new TeamLabRuntimeAsset { Kind = TeamLabResourceKind.Vm, ExecutionPlanJson = JsonSerializer.Serialize(declared) };
        var template = new ImageTemplate { VmNetworkMode = VmNetworkMode.Dhcp, OSType = OSType.Windows };
        Assert.Equal([AgentFeatureIds.TeamLabManagedGuestNetwork], TeamLabGuestNetworkCapabilityPolicy.ForAsset(declared with { VmNetworkMode = null }, runtime, template));
        Assert.Empty(TeamLabGuestNetworkCapabilityPolicy.ForAsset(declared with { VmNetworkMode = VmNetworkMode.Dhcp }, runtime, template));
        Assert.Empty(TeamLabGuestNetworkCapabilityPolicy.RequiredFeatures(VmNetworkMode.Preconfigured, OSType.Linux));
        Assert.Contains(AgentFeatureIds.CloudInit, TeamLabGuestNetworkCapabilityPolicy.RequiredFeatures(VmNetworkMode.ManagedStatic, OSType.Linux));
    }

    [Fact]
    public void PlanningPreview_RejectsOldAgentAndAcceptsExplicitCapabilities()
    {
        var definition = TeamLabTopologyV2Compiler.Compile(Definition());
        var requirements = new Dictionary<string, string[]> { ["vm"] = [AgentFeatureIds.TeamLabManagedGuestNetwork, AgentFeatureIds.CloudInit] };
        var old = new TeamLabPlanningNodeSnapshot(Guid.NewGuid(), "old", false, true, 0, 2, 0, 0);
        var error = Assert.Throws<TeamLabApiContractException>(() => TeamLabAssetPlanner.Build(Guid.NewGuid(), Guid.NewGuid(), definition, [old], requirements));
        Assert.Equal("teamlab_guest_network_capability_unavailable", error.Code);
        var capable = old with { Features = [AgentFeatureIds.TeamLabManagedGuestNetwork, AgentFeatureIds.CloudInit] };
        var plan = TeamLabAssetPlanner.Build(Guid.NewGuid(), Guid.NewGuid(), definition, [capable], requirements);
        Assert.Single(plan.Shards);
        Assert.Contains(AgentFeatureIds.TeamLabManagedGuestNetwork, plan.RequiredCapabilities);
    }

    [Fact]
    public void LegacyExecutionJson_ReencodesWithoutNewFields()
    {
        const string legacy = """{"AssetKey":"vm","Kind":"vm","ResourceId":"domain","ImageDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","DomainIdentity":"domain","TemplateId":1,"Cpu":10,"MemoryMiB":512,"NetworkAttachments":[{"NetworkKey":"entry","PortKey":"vm:nic","InterfaceName":"eth0","IpAddress":"10.48.0.10","GatewayIp":"10.48.0.1","Primary":true}],"HealthChecks":[],"ImageReference":null}""";
        var asset = JsonSerializer.Deserialize<TeamLabAssetExecutionSpecV2>(legacy)!;
        Assert.Equal(legacy, JsonSerializer.Serialize(asset));
        Assert.Equal(TeamLabGuestNetworkMode.Dhcp, asset.NetworkMode);
        const string leaseJson = """{"MacAddress":"02:00:00:00:00:01","IpAddress":"10.48.0.10","Hostname":"vm"}""";
        Assert.Equal(leaseJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<TeamLabDhcpLeaseV2>(leaseJson)));
        Assert.Equal(0, (int)VmNetworkMode.Dhcp);
        Assert.Equal(1, (int)VmNetworkMode.Preconfigured);
        Assert.Equal(2, (int)VmNetworkMode.ManagedStatic);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Release_RoundTripsNullEmptyDnsAndTypedRoutes(int schema)
    {
        var source = Definition();
        var json = TeamLabReleaseCodec.Encode(schema, source);
        var decoded = TeamLabReleaseCodec.DecodeDefinition(schema, json);
        var execution = TeamLabReleaseCodec.DecodeExecution(schema, json);
        Assert.Equal(VmNetworkMode.ManagedStatic, decoded.Assets[0].VmNetworkMode);
        Assert.Equal("lan0", decoded.Assets[0].Interfaces[0].GuestInterfaceName);
        Assert.False(decoded.Assets[0].Interfaces[0].UseDefaultGateway);
        Assert.Empty(decoded.Assets[0].Interfaces[0].DnsServers!);
        Assert.Null(decoded.Assets[0].Interfaces[1].DnsServers);
        Assert.Equal(7, execution.Assets[0].Interfaces[0].StaticRoutes![0].Metric);
        Assert.Equal(json, TeamLabReleaseCodec.Encode(schema, decoded));
    }

    [Fact]
    public async Task DraftPersistence_RoundTripsAllGuestRequirements()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new TeamLabTopologyApplicationService(context, new TeamLabTopologyValidator(), null!,
            new TeamLabControlScopeService(context), new NodeCapacitySnapshotService(context));
        var definition = Definition();
        var owner = Guid.CreateVersion7();
        var result = await service.CreateDraftAsync(new CreateTeamLabTopologyModel(
            definition.Name, definition.Networks, definition.Assets, []), owner, CancellationToken.None);
        context.ChangeTracker.Clear();
        var read = await service.GetAsync(result.Id, owner, false, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(definition.Assets), JsonSerializer.Serialize(read.Definition.Assets));
        var iface = await context.TeamLabTopologyInterfaces.OrderBy(item => item.Key).FirstAsync();
        Assert.Equal("[]", iface.DnsServersJson);
        Assert.Equal("lan0", iface.GuestInterfaceName);
    }

    [Fact]
    public async Task Publish_FreezesInheritedTemplateMode_WithoutChangingDraft()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var definition = Definition() with { Assets = [Definition().Assets[0] with { VmNetworkMode = null }] };
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 1, Name = "managed-template", ImageType = ImageType.Qcow2, Status = ImageStatus.Ready,
            ImageHash = "sha256:" + new string('a', 64), VmNetworkMode = VmNetworkMode.ManagedStatic
        });
        await context.SaveChangesAsync();
        var authoring = new TeamLabTopologyApplicationService(context, new TeamLabTopologyValidator(), null!,
            new TeamLabControlScopeService(context), new NodeCapacitySnapshotService(context));
        var owner = Guid.CreateVersion7();
        var draft = await authoring.CreateDraftAsync(new CreateTeamLabTopologyModel(
            definition.Name, definition.Networks, definition.Assets, []), owner, CancellationToken.None);
        var topology = await context.TeamLabTopologies.Include(item => item.Networks)
            .Include(item => item.Assets).ThenInclude(item => item.Interfaces).ThenInclude(item => item.Network)
            .SingleAsync(item => item.PublicId == draft.Id);
        // No workers are registered: preparation records the release without external operations.
        var distribution = new ImageDistributionService(context, null!, null!, null!, null!, null!, null!, null!, null!);
        var releases = new TeamLabReleaseService(context, new TeamLabTopologyValidator(), new TeamLabReleaseImagePreparationService(context, distribution));
        var released = await releases.PublishAsync(topology, draft.Revision, owner, null, CancellationToken.None);
        var snapshot = await context.TeamLabTopologyReleases.SingleAsync(item => item.Id == released.Id);
        Assert.Equal(VmNetworkMode.ManagedStatic, TeamLabReleaseCodec.DecodeExecution(2, snapshot.CanonicalJson).Assets[0].VmNetworkMode);
        Assert.Null(topology.Assets[0].VmNetworkMode);
        context.ImageTemplates.Single().VmNetworkMode = VmNetworkMode.Dhcp;
        await context.SaveChangesAsync();
        Assert.Equal(VmNetworkMode.ManagedStatic, TeamLabReleaseCodec.DecodeExecution(2, snapshot.CanonicalJson).Assets[0].VmNetworkMode);
    }

    [Fact]
    public void Validator_RejectsUnsafeInputsAndMultipleGateways()
    {
        var valid = Definition();
        Assert.True(new TeamLabTopologyValidator().Validate(valid).Valid);
        var asset = valid.Assets[0];
        var invalid = valid with { Assets = [asset with { Interfaces = [
            asset.Interfaces[0] with { GuestInterfaceName = "eth0;id", UseDefaultGateway = true, DnsServers = ["0.0.0.0"],
                StaticRoutes = [new("0.0.0.0/0", "10.48.0.1")] },
            asset.Interfaces[1] with { UseDefaultGateway = true }] }] };
        var result = new TeamLabTopologyValidator().Validate(invalid);
        Assert.Contains(result.Issues, issue => issue.Code == "guest_interface_name_invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "guest_dns_invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "guest_routes_invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "guest_default_gateway_multiple");
        Assert.True(TeamLabGuestNetworkValidation.IsDnsServer("127.0.0.1"));
    }

    [Fact]
    public void RuntimeUpdates_NetworkRequirementsTriggerReplacement()
    {
        var current = TeamLabTopologyV2Compiler.Compile(Definition());
        var changes = new[]
        {
            current.Assets[0] with { VmNetworkMode = VmNetworkMode.Dhcp },
            current.Assets[0] with { Interfaces = [current.Assets[0].Interfaces[0] with { DnsServers = ["127.0.0.1"] }, current.Assets[0].Interfaces[1]] },
            current.Assets[0] with { Interfaces = [current.Assets[0].Interfaces[0] with { GuestInterfaceName = "newname" }, current.Assets[0].Interfaces[1]] },
            current.Assets[0] with { Interfaces = [current.Assets[0].Interfaces[0] with { StaticRoutes = [] }, current.Assets[0].Interfaces[1]] },
            current.Assets[0] with { Interfaces = [current.Assets[0].Interfaces[0] with { UseDefaultGateway = true }, current.Assets[0].Interfaces[1]] }
        };
        foreach (var asset in changes)
            Assert.Equal("replace", Assert.Single(TeamLabRuntimeUpdateService.BuildChanges(current, current with { Assets = [asset] })).Action);
    }

    [Fact]
    public void ResetAssetPlan_PreservesRequirementsAndFreezesInheritedMode()
    {
        var definition = TeamLabTopologyV2Compiler.Compile(Definition());
        var asset = definition.Assets[0] with { VmNetworkMode = null };
        var networks = new Dictionary<string, TeamLabRuntimeNetwork>
        {
            ["entry"] = new() { TopologyKey = "entry", Cidr = "10.48.0.0/24" },
            ["core"] = new() { TopologyKey = "core", Cidr = "10.49.0.0/24" }
        };
        var result = TeamLabRuntimePlanner.CreateRuntimeAsset(new TeamLabRuntime { Id = 7, Generation = 2 }, asset,
            "entry,core", networks, new ImageTemplate { Id = 1, VmNetworkMode = VmNetworkMode.ManagedStatic });
        var frozen = JsonSerializer.Deserialize<TeamLabExecutionAsset>(result.ExecutionPlanJson)!;
        Assert.Equal(2, result.Generation);
        Assert.Equal(VmNetworkMode.ManagedStatic, frozen.VmNetworkMode);
        Assert.Equal("lan0", frozen.Interfaces[0].GuestInterfaceName);
        Assert.Empty(frozen.Interfaces[0].DnsServers!);
        Assert.Null(frozen.Interfaces[1].DnsServers);
        Assert.Equal(7, frozen.Interfaces[0].StaticRoutes![0].Metric);
    }

    [Fact]
    public void Compiler_BindsCurrentMacAndPerInterfaceOptions_AndDhcpDigestChanges()
    {
        var staticRequest = Request(VmNetworkMode.ManagedStatic);
        var plan = Compile(staticRequest);
        Assert.True(plan.IsValid(out var error), error);
        var attachment = Assert.Single(plan.Assets).NetworkAttachments[0];
        Assert.Equal("nic", attachment.InterfaceKey);
        Assert.Equal("02:00:00:00:00:01", attachment.MacAddress);
        Assert.Equal(24, attachment.PrefixLength);
        Assert.Null(attachment.GatewayIp);
        Assert.Empty(attachment.DnsServers!);
        Assert.Empty(plan.Networks[0].DhcpLeases!);
        var dhcp = Request(VmNetworkMode.Dhcp);
        var first = Compile(dhcp);
        var second = Compile(dhcp with { Interfaces = [dhcp.Interfaces[0] with { DnsServers = ["127.0.0.1"] }] });
        Assert.True(first.IsValid(out error), error);
        Assert.True(second.IsValid(out error), error);
        Assert.NotEqual(first.NetworkDigest, second.NetworkDigest);
        Assert.False(first.Networks[0].DhcpLeases![0].UseDefaultGateway);
        Assert.Empty(first.Networks[0].DhcpLeases![0].DnsServers!);
    }

    [Fact]
    public void SharedValidation_RequiresManagedIdentityAndRejectsForeignNextHop()
    {
        var asset = Compile(Request(VmNetworkMode.ManagedStatic)).Assets[0];
        Assert.True(TeamLabGuestNetworkValidation.IsValid(asset));
        var attachment = asset.NetworkAttachments[0];
        Assert.False(TeamLabGuestNetworkValidation.IsValid(asset with { NetworkAttachments = [attachment with { MacAddress = null }] }));
        Assert.False(TeamLabGuestNetworkValidation.IsValid(asset with { NetworkAttachments = [attachment with { StaticRoutes = [new("10.49.0.0/24", "10.50.0.1")] }] }));
        Assert.False(TeamLabGuestNetworkValidation.IsValid(asset with { NetworkAttachments = [attachment with { UseDefaultGateway = true, GatewayIp = null }] }));
    }

    static TeamLabTopologyDefinitionModel Definition() => new("Managed network",
        [new("entry", "Entry", new("10.48.0.0/16", 24), true), new("core", "Core", new("10.49.0.0/16", 24), false)],
        [new("vm", "VM", TeamLabAssetKind.Vm, 1, new(10, 512, 512),
            [new("nic", "entry", 10, true, GuestInterfaceName: "lan0", UseDefaultGateway: false, DnsServers: [], StaticRoutes: [new("10.50.0.0/24", "10.48.0.1", 7)]),
             new("nic2", "core", 10, false)], VmNetworkMode: VmNetworkMode.ManagedStatic)], []);

    static TeamLabNodeAssetCreateRequest Request(VmNetworkMode mode) => new(7, 11,
        Guid.Parse("019fa217-fcee-73af-bb45-1bc400000004"), 1, "vm", "VM", TeamLabAssetKind.Vm, 1, 10, 512, 512,
        null, true, new Dictionary<string, string>(), [new("nic", "entry", "bridge", "10.48.0.10", 24,
            "02:00:00:00:00:01", true, [], [], mode == VmNetworkMode.ManagedStatic ? "lan0" : null, false,
            [new("10.50.0.0/24", "10.48.0.1")])], VmNetworkMode: mode);

    static TeamLabExecutionPlanV2 Compile(TeamLabNodeAssetCreateRequest request) => TeamLabExecutionPlanCompiler.Compile(
        request.RuntimeId, request.RuntimePublicId, 1, "shard", true,
        new(7, 1, 1, "router", [new(new("entry", "Entry", "10.48.0.0/24", "10.48.0.1", "bridge"), "dns",
            [new("vm", "10.48.0.10", "02:00:00:00:00:01")])], [], new("", "", "", "", "", [], []), [], []),
        [request], [request], [], new Dictionary<int, string> { [1] = "sha256:" + new string('a', 64) });
}
