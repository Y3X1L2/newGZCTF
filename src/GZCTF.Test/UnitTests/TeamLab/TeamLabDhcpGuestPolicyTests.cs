using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabDhcpGuestPolicyTests
{
    [Fact]
    public void ExplicitNetworkDefaultsReuseTheSharedDhcpRow()
    {
        var operations = Provider().BuildApplyOperations(Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", true, ["10.0.1.2"], [])));
        Assert.Single(operations.Where(operation => operation["table"]!.GetValue<string>() == "DHCP_Options"));
    }

    [Fact]
    public void NoGatewayAndNoDnsAreOmittedFromOnlyThatPortOptions()
    {
        var plan = Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", false, []));
        var operations = Provider().BuildApplyOperations(plan);
        var dhcp = operations.Where(operation => operation["table"]!.GetValue<string>() == "DHCP_Options").ToArray();
        Assert.Equal(2, dhcp.Length);
        var custom = dhcp.Single(operation => Key(operation).Contains(":dhcp:", StringComparison.Ordinal));
        var shared = dhcp.Single(operation => Key(operation) == "net");
        Assert.Null(Option(custom, "router"));
        Assert.Null(Option(custom, "dns_server"));
        Assert.Equal("10.0.1.1", Option(shared, "router"));
        Assert.Equal("10.0.1.2", Option(shared, "dns_server"));
        var port = operations.Single(operation => operation["table"]!.GetValue<string>() == "Logical_Switch_Port" &&
            operation["row"]!["name"]!.GetValue<string>() == TeamLabOvnNaming.LogicalPortId(plan, "net", "nic"));
        Assert.Equal(custom["uuid-name"]!.GetValue<string>(), port["row"]!["dhcpv4_options"]![1]!.GetValue<string>());
    }

    [Fact]
    public void MultipleResolversUseTheOvnAddressListFormat()
    {
        var operations = Provider().BuildApplyOperations(Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", true, ["10.0.1.2", "10.0.1.3"])));
        var custom = operations.Single(operation => operation["table"]!.GetValue<string>() == "DHCP_Options" && Key(operation).Contains(":dhcp:", StringComparison.Ordinal));
        Assert.Equal("{10.0.1.2,10.0.1.3}", Option(custom, "dns_server"));
    }

    [Theory]
    [InlineData(true, "{0.0.0.0/0,10.0.1.1,10.20.0.0/16,10.0.1.5}")]
    [InlineData(false, "{10.20.0.0/16,10.0.1.5}")]
    public void ClasslessRoutesIncludeDefaultOnlyWhenRequested(bool gateway, string expected)
    {
        var operations = Provider().BuildApplyOperations(Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", gateway,
            ["10.0.1.2"], [new("10.20.0.0/16", "10.0.1.5")])));
        var custom = operations.Single(operation => operation["table"]!.GetValue<string>() == "DHCP_Options" && Key(operation).Contains(":dhcp:", StringComparison.Ordinal));
        Assert.Equal(expected, Option(custom, "classless_static_route"));
    }

    [Fact]
    public void RevisionFromSharedToPrivatePolicyChangesTheExistingPortReference()
    {
        var current = Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm"));
        var desired = Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", false, []));
        var operations = Provider().BuildRevisionOperations(current, desired, Rows(current, false));
        var inserted = operations.Single(operation => operation["op"]!.GetValue<string>() == "insert" && operation["table"]!.GetValue<string>() == "DHCP_Options");
        var updated = operations.Single(operation => operation["table"]!.GetValue<string>() == "Logical_Switch_Port");
        Assert.Equal(inserted["uuid-name"]!.GetValue<string>(), updated["row"]!["dhcpv4_options"]![1]!.GetValue<string>());
        Assert.DoesNotContain(operations, operation => operation["op"]!.GetValue<string>() == "delete");
    }

    [Fact]
    public void RevisionBackToSharedPolicyRepointsBeforeDeletingPrivateOptions()
    {
        var current = Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", false, []));
        var desired = Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", true, ["10.0.1.2"]));
        var rows = Rows(current, true);
        var operations = Provider().BuildRevisionOperations(current, desired, rows).ToList();
        var updateIndex = operations.FindIndex(operation => operation["table"]!.GetValue<string>() == "Logical_Switch_Port");
        var deleteIndex = operations.FindIndex(operation => operation["table"]!.GetValue<string>() == "DHCP_Options" && operation["op"]!.GetValue<string>() == "delete");
        Assert.True(updateIndex >= 0 && deleteIndex > updateIndex);
        Assert.Equal("uuid", operations[updateIndex]["row"]!["dhcpv4_options"]![0]!.GetValue<string>());
        Assert.Equal(rows[1][0]!["_uuid"]![1]!.GetValue<string>(), operations[updateIndex]["row"]!["dhcpv4_options"]![1]!.GetValue<string>());
    }

    [Fact]
    public void RevisionOfPrivateDnsPreservesItsRowAndPortIdentity()
    {
        var current = Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", false, []));
        var desired = Plan(new("02:00:00:00:00:01", "10.0.1.10", "vm", false, ["127.0.0.1"]));
        var operations = Provider().BuildRevisionOperations(current, desired, Rows(current, true));
        Assert.DoesNotContain(operations, operation => operation["op"]!.GetValue<string>() is "insert" or "delete");
        Assert.DoesNotContain(operations, operation => operation["table"]!.GetValue<string>() == "Logical_Switch_Port");
        Assert.Contains(operations, operation => operation["table"]!.GetValue<string>() == "DHCP_Options" && Option(operation, "dns_server") == "127.0.0.1");
    }

    [Theory]
    [InlineData(VmNetworkMode.ManagedStatic, VmNetworkMode.Dhcp, VmNetworkMode.Preconfigured, VmNetworkMode.ManagedStatic)]
    [InlineData(VmNetworkMode.Dhcp, VmNetworkMode.ManagedStatic, VmNetworkMode.Preconfigured, VmNetworkMode.Dhcp)]
    [InlineData(null, VmNetworkMode.ManagedStatic, VmNetworkMode.Preconfigured, VmNetworkMode.ManagedStatic)]
    [InlineData(null, null, VmNetworkMode.Preconfigured, VmNetworkMode.Preconfigured)]
    public void LeaseModeUsesDeclaredThenFrozenThenTemplateMode(VmNetworkMode? declared,
        VmNetworkMode? frozen, VmNetworkMode template, VmNetworkMode expected)
    {
        var configured = ExecutionAsset(declared);
        var topology = new TeamLabExecutionTopology(2, "test", [], [], [configured], [], new(false, false));
        var asset = new TeamLabRuntimeAsset { TopologyKey = "vm", SourceTemplateId = 7,
            ExecutionPlanJson = JsonSerializer.Serialize(ExecutionAsset(frozen)) };
        Assert.Equal(expected, TeamLabRouteApplicationService.ResolveAssetNetworkMode(asset, topology,
            new Dictionary<int, VmNetworkMode> { [7] = template }));
    }

    static TeamLabExecutionAsset ExecutionAsset(VmNetworkMode? mode) =>
        new("vm", "VM", TeamLabAssetKind.Vm, 7, 1, 256, 1024, [], null, null, null, 0, VmNetworkMode: mode);

    static TeamLabExecutionPlanV2 Plan(TeamLabDhcpLeaseV2 lease)
    {
        const string digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var attachment = new TeamLabAssetNetworkAttachmentV2("net", "nic", "eth0", "10.0.1.10/24",
            UseDefaultGateway: lease.UseDefaultGateway, DnsServers: lease.DnsServers, StaticRoutes: lease.StaticRoutes);
        return new(7, Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001"), 1, "node-a", digest, digest, false,
            [new("net", "10.0.1.0/24", "10.0.1.1", [new("nic", "vm", lease.MacAddress, lease.IpAddress)], [], [],
                DhcpLeases: [lease], DnsServerIp: "10.0.1.2")],
            [new("vm", "docker", "vm", digest, null, 7, 1, 256, [attachment], [])], []);
    }

    static JsonArray[] Rows(TeamLabExecutionPlanV2 plan, bool custom)
    {
        var options = new JsonArray { Row("net") };
        if (custom) options.Add(Row("net:dhcp:02:00:00:00:00:01"));
        return [[new JsonObject { ["name"] = TeamLabOvnNaming.LogicalPortId(plan, "net", "nic"),
            ["_uuid"] = new JsonArray("uuid", Guid.NewGuid().ToString()) }], options, []];
    }

    static JsonObject Row(string key) => new()
    {
        ["_uuid"] = new JsonArray("uuid", Guid.NewGuid().ToString()),
        ["external_ids"] = new JsonArray("map", new JsonArray { new JsonArray("gzctf-key", key) })
    };

    static string Key(JsonObject operation) => Map(operation["row"]!["external_ids"]!, "gzctf-key")!;
    static string? Option(JsonObject operation, string key) => Map(operation["row"]?["options"], key);
    static string? Map(JsonNode? map, string key) => map is JsonArray entries && entries[1] is JsonArray values
        ? values.OfType<JsonArray>().SingleOrDefault(pair => pair[0]?.GetValue<string>() == key)?[1]?.GetValue<string>() : null;
    static TeamLabOvnNetworkProvider Provider() => new(new OvsdbJsonRpcClient(),
        Options.Create(new AgentTeamLabConfig { ManagedDhcpLeaseSeconds = 3600 }), NullLogger<TeamLabOvnNetworkProvider>.Instance);
}
