using System;
using System.Linq;
using System.Text.Json.Nodes;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabOvnNetworkProviderTests
{
    [Fact]
    public void ApplyOperations_EncodeMapsInOvsdbFormat()
    {
        var provider = Provider();
        var operations = provider.BuildApplyOperations(Plan());

        Assert.NotEmpty(operations);
        foreach (var operation in operations)
        {
            var row = operation["row"] as JsonObject;
            if (row is null)
                continue;
            foreach (var key in new[] { "external_ids", "options", "records" })
            {
                if (row[key] is not JsonArray map)
                    continue;
                Assert.Equal("map", map[0]?.GetValue<string>());
                Assert.IsType<JsonArray>(map[1]);
            }
        }
    }

    [Fact]
    public void ApplyOperations_UseValidOvsdbUuidNames()
    {
        var operations = Provider().BuildApplyOperations(Plan());

        Assert.NotEmpty(operations);
        foreach (var operation in operations)
        {
            var uuidName = operation["uuid-name"]?.GetValue<string>();
            Assert.True(IsOvsdbId(uuidName), $"uuid-name is not a valid OVSDB id: {uuidName}");
            AssertJsonUuids(operation);
            var row = operation["row"] as JsonObject;
            if (string.Equals(operation["table"]?.GetValue<string>(), "Logical_Switch_Port", StringComparison.Ordinal))
            {
                Assert.Null(row?["switch"]);
                if (string.Equals(ExternalId(row, "gzctf-asset-key"), "player-gateway", StringComparison.Ordinal))
                {
                    Assert.Null(row?["type"]);
                    Assert.Equal("02:00:00:00:00:fe 10.0.1.254",
                        (row?["addresses"] as JsonArray)?[1]?[0]?.GetValue<string>());
                }
            }
            if (string.Equals(operation["table"]?.GetValue<string>(), "Logical_Router_Static_Route", StringComparison.Ordinal))
                Assert.IsType<string>((operation["row"] as JsonObject)?["output_port"]?.GetValue<string>());
        }
    }

    [Fact]
    public void ApplyOperations_LogicalPortNamesAreUuidInterfaceIds()
    {
        var operations = Provider().BuildApplyOperations(Plan());
        var names = operations
            .Where(operation => string.Equals(operation["table"]?.GetValue<string>(),
                "Logical_Switch_Port", StringComparison.Ordinal))
            .Select(operation => operation["row"] as JsonObject)
            .Where(row => row is not null && row["type"] is null)
            .Select(row => row!["name"]?.GetValue<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name));
        Assert.Equal(2, names.Count());
        Assert.All(names, name => Assert.True(Guid.TryParse(name, out _), $"LSP name is not a UUID: {name}"));
    }

    [Fact]
    public void LogicalPortId_IsDeterministicAndUuidFormatted()
    {
        var plan = Plan();
        var first = TeamLabOvnNaming.LogicalPortId(plan, "network-a", "port-a");
        var second = TeamLabOvnNaming.LogicalPortId(plan, "network-a", "port-a");

        Assert.True(Guid.TryParse(first, out _));
        Assert.Equal(first, second);
        Assert.NotEqual(first, TeamLabOvnNaming.LogicalPortId(plan, "network-a", "port-b"));
    }

    static void AssertJsonUuids(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            if (array.Count == 2 && array[0] is JsonValue first &&
                string.Equals(first.GetValue<string>(), "named-uuid", StringComparison.Ordinal))
            {
                Assert.True(IsOvsdbId(array[1]?.GetValue<string>()),
                    $"named-uuid is not a valid OVSDB id: {array[1]}");
                return;
            }
            foreach (var item in array)
                AssertJsonUuids(item);
        }
        else if (node is JsonObject obj)
        {
            foreach (var property in obj)
                AssertJsonUuids(property.Value);
        }
    }

    [Fact]
    public void RemoveOperations_DeleteChildrenBeforeTheirParents()
    {
        var operations = TeamLabOvnNetworkProvider.BuildRemoveOperations(Plan());

        int IndexOf(string table) => operations
            .Select((operation, index) => (operation, index))
            .First(item => string.Equals(item.operation["op"]?.GetValue<string>(), "delete", StringComparison.Ordinal) &&
                            string.Equals(item.operation["table"]?.GetValue<string>(), table, StringComparison.Ordinal))
            .index;
        Assert.True(IndexOf("Logical_Router_Policy") < IndexOf("Logical_Router"));
        Assert.True(IndexOf("Logical_Router_Static_Route") < IndexOf("Logical_Router"));
        Assert.True(IndexOf("Logical_Router_Port") < IndexOf("Logical_Router"));
        Assert.True(IndexOf("ACL") < IndexOf("Logical_Switch"));
        Assert.True(IndexOf("DNS") < IndexOf("Logical_Switch"));
        Assert.True(IndexOf("Logical_Switch_Port") < IndexOf("Logical_Switch"));
        Assert.True(IndexOf("Logical_Switch_Port") < IndexOf("DHCP_Options"));
    }

    [Fact]
    public void RemoveOperations_DeleteByOwnershipNotByMissingNameColumns()
    {
        var plan = Plan();
        var operations = TeamLabOvnNetworkProvider.BuildRemoveOperations(plan);

        Assert.Equal(16, operations.Count);
        foreach (var operation in operations)
        {
            var condition = (operation["where"] as JsonArray)?[0] as JsonArray;
            Assert.Equal("external_ids", condition?[0]?.GetValue<string>());
            Assert.Equal("includes", condition?[1]?.GetValue<string>());
            var entries = (condition?[2] as JsonArray)?[1] as JsonArray;
            var digest = entries?.OfType<JsonArray>()
                .FirstOrDefault(entry => string.Equals(entry[0]?.GetValue<string>(), "gzctf-network-digest",
                    StringComparison.Ordinal));
            Assert.NotNull(digest);
            Assert.Equal(plan.NetworkDigest, digest![1]?.GetValue<string>());
        }
        foreach (var update in operations.Take(7))
        {
            Assert.Equal("update", update["op"]?.GetValue<string>());
            var row = update["row"] as JsonObject;
            Assert.NotNull(row);
            var value = row!.First().Value as JsonArray;
            Assert.Equal("set", value?[0]?.GetValue<string>());
            Assert.Empty(value![1]!.AsArray());
        }
        foreach (var delete in operations.Skip(7))
        {
            Assert.Equal("delete", delete["op"]?.GetValue<string>());
            Assert.NotNull(delete["table"]);
        }
    }

    [Theory]
    [InlineData("interface", "docker-switch-nic")]
    [InlineData("port", "docker-switch-nic")]
    [InlineData("port", "eth0")]
    public void OvsdbId_AlwaysProducesValidUuidName(string kind, string value)
    {
        Assert.True(IsOvsdbId(TeamLabOvnNaming.OvsdbId(kind, value)),
            $"generated id is not a valid OVSDB id: {TeamLabOvnNaming.OvsdbId(kind, value)}");
    }

    static string? ExternalId(JsonObject? row, string key) =>
        row?["external_ids"] is JsonArray map && map[1] is JsonArray entries
            ? (entries.FirstOrDefault(entry => (entry as JsonArray)?[0]?.GetValue<string>() == key) as JsonArray)?[1]?.GetValue<string>()
            : null;
    static bool IsOvsdbId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    static TeamLabOvnNetworkProvider Provider() => new(
        new OvsdbJsonRpcClient(),
        Options.Create(new AgentTeamLabConfig { ManagedDhcpLeaseSeconds = 3600 }),
        NullLogger<TeamLabOvnNetworkProvider>.Instance);

    static TeamLabExecutionPlanV2 Plan()
    {
        var plan = new TeamLabExecutionPlanV2(
            7,
            Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001"),
            1,
            "node-a",
            string.Empty,
            string.Empty,
            false,
            [new TeamLabNetworkIntentV2(
                "network-a", "10.0.1.0/24", "10.0.1.1",
                [new TeamLabNetworkPortV2("port-a", "docker-1", "02:00:00:00:00:01", "10.0.1.10")],
                [new TeamLabNetworkRouteV2("10.0.2.0/24", "10.0.1.2")],
                [new TeamLabNetworkPolicyV2("10.0.1.0/24", "10.0.2.0/24", "tcp", 443, true)],
                DhcpLeases: [new TeamLabDhcpLeaseV2("02:00:00:00:00:02", "10.0.1.20", "docker-1")],
                DnsRecords: [new TeamLabDnsRecordV2("docker-1", "10.0.1.10")],
                PlayerGateway: new TeamLabPlayerGatewayV2("player-gateway", "02:00:00:00:00:fe", "10.0.1.254", "tlwg-test"))],
            [Asset("docker-1", "network-a", "port-a")],
            [],
            new TeamLabNetworkControlIntentV2(
                [new TeamLabRouterIntentV2("router", ["network-a"])],
                [new TeamLabForwardPolicyV2("10.0.1.0/24", "10.0.2.0/24", true)]));
        return plan with
        {
            NetworkDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PlanDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };
    }

    static TeamLabAssetExecutionSpecV2 Asset(string key, string network, string port) => new(
        key, "docker", key, "registry.example/teamlab@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        null, 3, 1, 256,
        [new TeamLabAssetNetworkAttachmentV2(network, port, "eth0", "10.0.1.10/24")], []);
}
