using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

public static class TeamLabReleaseCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    static TeamLabReleaseCodec() => JsonOptions.Converters.Add(new JsonStringEnumConverter());

    public static string Encode(TeamLabTopologyDefinitionModel definition)
    {
        var canonical = Normalize(definition);
        return JsonSerializer.Serialize(canonical, JsonOptions);
    }

    public static TeamLabTopologyDefinitionModel Decode(string canonicalJson) =>
        JsonSerializer.Deserialize<TeamLabTopologyDefinitionModel>(canonicalJson, JsonOptions)
        ?? throw new TeamLabApiContractException("release_invalid", "The topology release payload is invalid.", 500);

    public static string ComputeContentHash(int schemaVersion, string canonicalJson)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion, topology = JsonDocument.Parse(canonicalJson).RootElement }, JsonOptions);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(payload))}";
    }

    public static TeamLabTopologyDefinitionModel Normalize(TeamLabTopologyDefinitionModel definition) =>
        new(
            definition.Name.Trim(),
            definition.Networks
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item with
                {
                    Key = item.Key.Trim(),
                    Name = item.Name.Trim(),
                    AddressPool = new TeamLabAddressPoolModel(
                        item.AddressPool.PoolCidr.Trim(), item.AddressPool.RuntimePrefixLength)
                })
                .ToArray(),
            definition.Assets
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item with
                {
                    Key = item.Key.Trim(),
                    Name = item.Name.Trim(),
                    Interfaces = item.Interfaces
                        .OrderBy(iface => iface.Key, StringComparer.Ordinal)
                        .Select(iface => iface with
                        {
                            Key = iface.Key.Trim(),
                            NetworkKey = iface.NetworkKey.Trim()
                        })
                        .ToArray(),
                    Environment = item.Environment is null
                        ? null
                        : item.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.Ordinal)
                })
                .ToArray(),
            definition.Connections
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item with
                {
                    Key = item.Key.Trim(),
                    FromNetworkKey = item.FromNetworkKey.Trim(),
                    ToNetworkKey = item.ToNetworkKey.Trim(),
                    ViaAssetKey = item.ViaAssetKey.Trim()
                })
                .ToArray());
}
