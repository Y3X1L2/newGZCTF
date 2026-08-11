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
            var bridgeRows = await ovsdb.SelectAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                "Bridge", Where("name", config.OvsIntegrationBridgeName), cancellationToken);
            if (bridgeRows.Count == 0)
                return TeamLabAttachmentResult.Failed("network",
                    $"OVS integration bridge {config.OvsIntegrationBridgeName} does not exist.");

            var interfaceRows = await ovsdb.SelectAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                "Interface", Where("name", interfaceName), cancellationToken);
            var portRows = await ovsdb.SelectAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                "Port", Where("name", interfaceName), cancellationToken);
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
            var portRows = await ovsdb.SelectAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                "Port", Where("name", interfaceName), cancellationToken);
            var portUuid = portRows.Count == 0 ? null : ExistingUuid(portRows, plan, networkKey, "port");
            var operations = new List<JsonObject>();
            if (portUuid is not null)
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
                ["where"] = Where("name", interfaceName)
            });
            operations.Add(new JsonObject
            {
                ["op"] = "delete",
                ["table"] = "Interface",
                ["where"] = Where("name", interfaceName)
            });
            await ovsdb.TransactAsync(config.OvsLocalEndpoint, config.OvsLocalDatabase,
                operations, cancellationToken);
            return new TeamLabAttachmentResult(true, "Attachment removed.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return TeamLabAttachmentResult.Failed("cleanup", exception.Message);
        }
    }

    static JsonArray Where(string column, string value) =>
        new() { new JsonArray { column, "==", value } };

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
}

public sealed record TeamLabAttachmentResult(bool Success, string Message)
{
    public static TeamLabAttachmentResult Failed(string _, string message) => new(false, message);
}
