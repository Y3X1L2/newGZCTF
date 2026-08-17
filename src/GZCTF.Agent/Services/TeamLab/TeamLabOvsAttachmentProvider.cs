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

    public Task<TeamLabAttachmentResult> AttachAsync(
        TeamLabExecutionPlanV2 plan,
        string interfaceName,
        string networkKey,
        string portKey,
        CancellationToken cancellationToken) =>
        AttachCoreAsync(
            plan.RuntimePublicId,
            plan.Generation,
            interfaceName,
            networkKey,
            TeamLabOvnNaming.LogicalPortId(plan, networkKey, portKey),
            plan.PlanDigest,
            cancellationToken);

    public Task<TeamLabAttachmentResult> AttachHostInterfaceAsync(
        Guid runtimePublicId,
        int generation,
        string interfaceName,
        string networkKey,
        string portKey,
        CancellationToken cancellationToken) =>
        AttachCoreAsync(
            runtimePublicId,
            generation,
            interfaceName,
            networkKey,
            TeamLabOvnNaming.LogicalPortId(runtimePublicId, generation, networkKey, portKey),
            null,
            cancellationToken);

    async Task<TeamLabAttachmentResult> AttachCoreAsync(
        Guid runtimePublicId,
        int generation,
        string interfaceName,
        string networkKey,
        string logicalPortName,
        string? planDigest,
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
            var interfaceUuid = ExistingUuid(interfaceRows, runtimePublicId, generation, networkKey, planDigest, "interface");
            var portUuid = ExistingUuid(portRows, runtimePublicId, generation, networkKey, planDigest, "port");
            var externalIds = BuildExternalIds(runtimePublicId, generation, networkKey, logicalPortName, planDigest);
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
                        ["type"] = "system",
                        ["external_ids"] = externalIds
                    }
                });
            else
                operations.Add(UpdateSystemInterface(interfaceName, externalIds));

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
            return TeamLabAttachmentResult.Failed("network", $"OVS attachment transaction failed: {Trim(exception.Message)}");
        }
    }

    public Task<TeamLabAttachmentResult> RemoveAsync(
        TeamLabExecutionPlanV2 plan,
        string interfaceName,
        string networkKey,
        CancellationToken cancellationToken) =>
        RemoveCoreAsync(plan.RuntimePublicId, plan.Generation, interfaceName, networkKey, plan.PlanDigest, cancellationToken);

    public Task<TeamLabAttachmentResult> RemoveHostInterfaceAsync(
        Guid runtimePublicId,
        int generation,
        string interfaceName,
        string networkKey,
        CancellationToken cancellationToken) =>
        RemoveCoreAsync(runtimePublicId, generation, interfaceName, networkKey, null, cancellationToken);

    async Task<TeamLabAttachmentResult> RemoveCoreAsync(
        Guid runtimePublicId,
        int generation,
        string interfaceName,
        string networkKey,
        string? planDigest,
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
            var portUuid = portRows.Count == 0
                ? null
                : ExistingUuid(portRows, runtimePublicId, generation, networkKey, planDigest, "port");
            if (portUuid is null)
                return new TeamLabAttachmentResult(true, "Attachment is already absent.");
            if (ExistingUuid(interfaceRows, runtimePublicId, generation, networkKey, planDigest, "interface") is null)
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
                ["where"] = OwnedWhere("name", interfaceName, runtimePublicId, generation, networkKey, planDigest)
            });
            operations.Add(new JsonObject
            {
                ["op"] = "delete",
                ["table"] = "Interface",
                ["where"] = OwnedWhere("name", interfaceName, runtimePublicId, generation, networkKey, planDigest)
            });
            var result = await ovsdb.TransactAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                operations, cancellationToken);
            RequireCleanupCount(result, 1, "Port", interfaceName);
            RequireCleanupCount(result, 2, "Interface", interfaceName);
            return new TeamLabAttachmentResult(true, "Attachment removed.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return TeamLabAttachmentResult.Failed("cleanup", $"OVS attachment cleanup transaction failed: {Trim(exception.Message)}");
        }
    }

    static JsonArray BuildExternalIds(
        Guid runtimePublicId,
        int generation,
        string networkKey,
        string logicalPortName,
        string? planDigest)
    {
        var pairs = new List<(string Key, string Value)>
        {
            ("gzctf-runtime", runtimePublicId.ToString("D")),
            ("gzctf-generation", generation.ToString()),
            ("gzctf-network-key", networkKey),
            ("iface-id", logicalPortName)
        };
        if (planDigest is not null)
            pairs.Add(("gzctf-plan-digest", planDigest));
        return OvsdbJsonCodec.Map(pairs.ToArray());
    }

    static string Trim(string value) => value.Length <= 512 ? value : value[..512];

    static JsonArray Where(string column, string value) =>
        new() { new JsonArray { column, "==", value } };

    static JsonArray OwnedWhere(
        string column,
        string value,
        Guid runtimePublicId,
        int generation,
        string networkKey,
        string? planDigest)
    {
        var pairs = new List<(string Key, string Value)>
        {
            ("gzctf-runtime", runtimePublicId.ToString("D")),
            ("gzctf-generation", generation.ToString()),
            ("gzctf-network-key", networkKey)
        };
        if (planDigest is not null)
            pairs.Add(("gzctf-plan-digest", planDigest));
        return new JsonArray
        {
            new JsonArray { column, "==", value },
            new JsonArray
            {
                "external_ids",
                "includes",
                OvsdbJsonCodec.Map(pairs.ToArray())
            }
        };
    }

    static JsonArray Set(JsonArray value) => new() { "set", new JsonArray { value } };

    static JsonArray Uuid(string value) => new() { "uuid", value };

    static JsonArray NamedUuid(string kind, string name) =>
        new() { "named-uuid", NamedName(kind, name) };

    static string NamedName(string kind, string name) => TeamLabOvnNaming.OvsdbId(kind, name);

    static string? ExistingUuid(
        JsonArray rows,
        Guid runtimePublicId,
        int generation,
        string networkKey,
        string? planDigest,
        string kind)
    {
        if (rows.Count == 0) return null;
        var row = rows[0] as JsonObject
                  ?? throw new JsonException($"OVSDB {kind} row is invalid.");
        if (row["_uuid"] is not JsonArray uuid || uuid.Count != 2 || uuid[1]?.GetValue<string>() is not { } value)
            throw new JsonException($"OVSDB {kind} row has no UUID.");
        if (!string.Equals(OvsdbJsonCodec.GetMapValue(row["external_ids"], "gzctf-runtime"),
                runtimePublicId.ToString("D"), StringComparison.Ordinal) ||
            !string.Equals(OvsdbJsonCodec.GetMapValue(row["external_ids"], "gzctf-generation"),
                generation.ToString(), StringComparison.Ordinal) ||
            !string.Equals(OvsdbJsonCodec.GetMapValue(row["external_ids"], "gzctf-network-key"),
                networkKey, StringComparison.Ordinal) ||
            planDigest is not null && !string.Equals(
                OvsdbJsonCodec.GetMapValue(row["external_ids"], "gzctf-plan-digest"),
                planDigest, StringComparison.Ordinal))
            throw new InvalidOperationException($"OVS {kind} identity conflicts with the requested runtime.");
        return value;
    }

    static JsonObject UpdateSystemInterface(string name, JsonArray externalIds) => new()
    {
        ["op"] = "update",
        ["table"] = "Interface",
        ["where"] = Where("name", name),
        ["row"] = new JsonObject
        {
            ["type"] = "system",
            ["external_ids"] = externalIds
        }
    };

    static bool BridgeContainsPort(JsonObject? bridge, string portUuid)
    {
        if (bridge?["ports"] is not JsonArray ports)
            return false;
        if (ports.Count == 2 && string.Equals(ports[0]?.GetValue<string>(), "uuid", StringComparison.Ordinal))
            return string.Equals(ports[1]?.GetValue<string>(), portUuid, StringComparison.Ordinal);
        if (ports.Count == 2 && string.Equals(ports[0]?.GetValue<string>(), "set", StringComparison.Ordinal) &&
            ports[1] is JsonArray members)
            return members.OfType<JsonArray>().Any(port => port.Count == 2 &&
                string.Equals(port[0]?.GetValue<string>(), "uuid", StringComparison.Ordinal) &&
                string.Equals(port[1]?.GetValue<string>(), portUuid, StringComparison.Ordinal));
        return false;
    }

    static void RequireCleanupCount(JsonNode result, int index, string table, string name)
    {
        if (result is not JsonArray operations || operations[index]?["count"]?.GetValue<int>() is not (0 or 1))
            throw new InvalidOperationException($"OVSDB cleanup did not converge for owned {table} {name}.");
    }
}

public sealed record TeamLabAttachmentResult(bool Success, string Message)
{
    public static TeamLabAttachmentResult Failed(string _, string message) => new(false, message);
}