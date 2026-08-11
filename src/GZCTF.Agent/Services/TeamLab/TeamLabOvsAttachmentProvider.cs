using System.Text.Json.Nodes;
using System.Text.Json;
using GZCTF.Agent.Models;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabOvsAttachmentProvider(
    OvsdbJsonRpcClient ovsdb,
    IOptions<AgentTeamLabConfig> options)
{
    readonly AgentTeamLabConfig config = options.Value;

    public async Task<TeamLabAttachmentResult> AttachAsync(
        TeamLabExecutionPlanV2 plan,
        string interfaceName,
        string networkKey,
        string portKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.OvsLocalEndpoint))
            return TeamLabAttachmentResult.Failed("network", "Local OVS endpoint is not configured.");
        if (string.IsNullOrWhiteSpace(interfaceName) || string.IsNullOrWhiteSpace(networkKey))
            return TeamLabAttachmentResult.Failed("validation", "OVS attachment identity is invalid.");

        try
        {
            var state = await ovsdb.SelectAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                [("Bridge", Where("name", config.OvsIntegrationBridgeName)),
                 ("Interface", Where("name", interfaceName)),
                 ("Port", Where("name", interfaceName))], cancellationToken);
            var bridgeRows = state[0];
            if (bridgeRows.Count == 0)
                return TeamLabAttachmentResult.Failed("network",
                    $"OVS integration bridge {config.OvsIntegrationBridgeName} does not exist.");
            var interfaceRows = state[1];
            var portRows = state[2];
            var interfaceUuid = ExistingUuid(interfaceRows, plan, networkKey, "interface");
            var portUuid = ExistingUuid(portRows, plan, networkKey, "port");
            var externalIds = new JsonObject
            {
                ["gzctf-runtime"] = plan.RuntimePublicId.ToString("D"),
                ["gzctf-generation"] = plan.Generation.ToString(),
                ["gzctf-network-key"] = networkKey,
                ["iface-id"] = TeamLabOvnNaming.LogicalPortName(plan, networkKey, portKey)
            };
            var interfaceRef = interfaceUuid is null
                ? NamedUuid("interface", interfaceName)
                : Uuid(interfaceUuid);
            var portRef = portUuid is null
                ? NamedUuid("port", interfaceName)
                : Uuid(portUuid);
            var operations = new List<JsonObject>();
            if (interfaceUuid is null)
                operations.Add(new JsonObject
                {
                    ["op"] = "insert",
                    ["table"] = "Interface",
                    ["uuid-name"] = NamedName("interface", interfaceName),
                    ["row"] = new JsonObject
                    {
                        ["name"] = interfaceName,
                        ["type"] = "",
                        ["external_ids"] = externalIds
                    }
                });
            else
                operations.Add(Update("Interface", interfaceName, externalIds));

            if (portUuid is null)
                operations.Add(new JsonObject
                {
                    ["op"] = "insert",
                    ["table"] = "Port",
                    ["uuid-name"] = NamedName("port", interfaceName),
                    ["row"] = new JsonObject
                    {
                        ["name"] = interfaceName,
                        ["interfaces"] = Set(interfaceRef),
                        ["external_ids"] = externalIds.DeepClone()
                    }
                });
            else
                operations.Add(new JsonObject
                {
                    ["op"] = "update",
                    ["table"] = "Port",
                    ["where"] = Where("name", interfaceName),
                    ["row"] = new JsonObject
                    {
                        ["interfaces"] = Set(interfaceRef),
                        ["external_ids"] = externalIds.DeepClone()
                    }
                });

            operations.Add(new JsonObject
            {
                ["op"] = "mutate",
                ["table"] = "Bridge",
                ["where"] = Where("name", config.OvsIntegrationBridgeName),
                ["mutations"] = new JsonArray
                {
                    new JsonArray { "ports", "insert", Set(portRef) }
                }
            });
            await ovsdb.TransactAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                operations, cancellationToken);
            return new TeamLabAttachmentResult(true, "Attachment applied.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return TeamLabAttachmentResult.Failed("network", exception.Message);
        }
    }

    public async Task<TeamLabAttachmentResult> RemoveAsync(
        TeamLabExecutionPlanV2 plan,
        string interfaceName,
        string networkKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.OvsLocalEndpoint) || string.IsNullOrWhiteSpace(interfaceName))
            return TeamLabAttachmentResult.Failed("validation", "OVS attachment identity is invalid.");
        try
        {
            var state = await ovsdb.SelectAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                [("Bridge", Where("name", config.OvsIntegrationBridgeName)),
                 ("Port", Where("name", interfaceName)),
                 ("Interface", Where("name", interfaceName))], cancellationToken);
            var bridgeRows = state[0];
            if (bridgeRows.Count != 1)
                return TeamLabAttachmentResult.Failed("cleanup",
                    $"OVS integration bridge {config.OvsIntegrationBridgeName} does not exist.");
            var portRows = state[1];
            var interfaceRows = state[2];
            var portUuid = portRows.Count == 0 ? null : ExistingUuid(portRows, plan, networkKey, "port");
            if (portUuid is null)
                return new TeamLabAttachmentResult(true, "Attachment is already absent.");
            if (ExistingUuid(interfaceRows, plan, networkKey, "interface") is null)
                return TeamLabAttachmentResult.Failed("cleanup",
                    $"OVS port {interfaceName} has no owned interface.");
            if (!BridgeContainsPort(bridgeRows[0] as JsonObject, portUuid))
                return TeamLabAttachmentResult.Failed("cleanup",
                    $"OVS port {interfaceName} is not attached to {config.OvsIntegrationBridgeName}.");
            var operations = new List<JsonObject>();
            operations.Add(new JsonObject
            {
                ["op"] = "mutate",
                ["table"] = "Bridge",
                ["where"] = Where("name", config.OvsIntegrationBridgeName),
                ["mutations"] = new JsonArray { new JsonArray { "ports", "delete", Set(Uuid(portUuid)) } }
            });
            operations.Add(new JsonObject
            {
                ["op"] = "delete",
                ["table"] = "Port",
                ["where"] = OwnedWhere("name", interfaceName, plan, networkKey)
            });
            operations.Add(new JsonObject
            {
                ["op"] = "delete",
                ["table"] = "Interface",
                ["where"] = OwnedWhere("name", interfaceName, plan, networkKey)
            });
            var result = await ovsdb.TransactAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                operations, cancellationToken);
            RequireDeleteCount(result, 1, "Port", interfaceName);
            RequireDeleteCount(result, 2, "Interface", interfaceName);
            return new TeamLabAttachmentResult(true, "Attachment removed.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return TeamLabAttachmentResult.Failed("cleanup", exception.Message);
        }
    }

    static JsonArray Where(string column, string value) =>
        new() { new JsonArray { column, "==", value } };

    static JsonArray OwnedWhere(string column, string value, TeamLabExecutionPlanV2 plan, string networkKey) =>
        new()
        {
            new JsonArray { column, "==", value },
            new JsonArray { "external_ids", "includes", new JsonArray { "map", new JsonArray
            {
                new JsonArray { "gzctf-runtime", plan.RuntimePublicId.ToString("D") },
                new JsonArray { "gzctf-generation", plan.Generation.ToString() },
                new JsonArray { "gzctf-network-key", networkKey }
            } } }
        };

    static JsonArray Set(JsonArray value) => new() { "set", new JsonArray { value } };

    static JsonArray Uuid(string value) => new() { "uuid", value };

    static JsonArray NamedUuid(string kind, string name) =>
        new() { "named-uuid", NamedName(kind, name) };

    static string NamedName(string kind, string name) =>
        $"gzctf_{kind}_{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name))).ToLowerInvariant()[..16]}";

    static string? ExistingUuid(JsonArray rows, TeamLabExecutionPlanV2 plan, string networkKey, string kind)
    {
        if (rows.Count == 0) return null;
        var row = rows[0] as JsonObject
                  ?? throw new JsonException($"OVSDB {kind} row is invalid.");
        if (row["_uuid"] is not JsonArray uuid || uuid.Count != 2 || uuid[1]?.GetValue<string>() is not { } value)
            throw new JsonException($"OVSDB {kind} row has no UUID.");
        if (row["external_ids"] is not JsonObject externalIds ||
            !string.Equals(externalIds["gzctf-runtime"]?.GetValue<string>(), plan.RuntimePublicId.ToString("D"), StringComparison.Ordinal) ||
            !string.Equals(externalIds["gzctf-generation"]?.GetValue<string>(), plan.Generation.ToString(), StringComparison.Ordinal) ||
            !string.Equals(externalIds["gzctf-network-key"]?.GetValue<string>(), networkKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"OVS {kind} identity conflicts with the requested runtime.");
        return value;
    }

    static JsonObject Update(string table, string name, JsonObject externalIds) => new()
    {
        ["op"] = "update",
        ["table"] = table,
        ["where"] = Where("name", name),
        ["row"] = new JsonObject { ["external_ids"] = externalIds }
    };

    static bool BridgeContainsPort(JsonObject? bridge, string portUuid)
    {
        if (bridge?["ports"] is not JsonArray set || set.Count != 2 ||
            !string.Equals(set[0]?.GetValue<string>(), "set", StringComparison.Ordinal) ||
            set[1] is not JsonArray ports)
            return false;
        return ports.OfType<JsonArray>().Any(port => port.Count == 2 &&
            string.Equals(port[0]?.GetValue<string>(), "uuid", StringComparison.Ordinal) &&
            string.Equals(port[1]?.GetValue<string>(), portUuid, StringComparison.Ordinal));
    }

    static void RequireDeleteCount(JsonNode result, int index, string table, string name)
    {
        if (result is not JsonArray operations || operations[index]?["count"]?.GetValue<int>() != 1)
            throw new InvalidOperationException($"OVSDB did not remove owned {table} {name}.");
    }
}

public sealed record TeamLabAttachmentResult(bool Success, string Message)
{
    public static TeamLabAttachmentResult Failed(string _, string message) => new(false, message);
}
