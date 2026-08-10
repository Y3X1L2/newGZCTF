using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application;

public static class TeamLabReleaseCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    static TeamLabReleaseCodec() => JsonOptions.Converters.Add(new JsonStringEnumConverter());

    public static string Encode(TeamLabTopologyDefinitionModel definition) => Encode(1, definition, null);

    public static string Encode(
        int schemaVersion,
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyDictionary<string, string>? imageDigests = null)
    {
        var normalized = Normalize(definition);
        return schemaVersion switch
        {
            1 => JsonSerializer.Serialize(ToV1(normalized, imageDigests), JsonOptions),
            2 => JsonSerializer.Serialize(ToV2(normalized, imageDigests), JsonOptions),
            _ => throw UnsupportedSchema(schemaVersion)
        };
    }

    public static TeamLabTopologyDefinitionModel Decode(string canonicalJson) => DecodeDefinition(1, canonicalJson);

    public static TeamLabTopologyDefinitionModel DecodeDefinition(int schemaVersion, string canonicalJson) =>
        schemaVersion switch
        {
            1 => FromV1(JsonSerializer.Deserialize<TeamLabTopologyDefinitionV1Model>(canonicalJson, JsonOptions)
                        ?? throw InvalidRelease()),
            2 => FromV2(JsonSerializer.Deserialize<TeamLabTopologyDefinitionV2Model>(canonicalJson, JsonOptions)
                        ?? throw InvalidRelease()),
            _ => throw UnsupportedSchema(schemaVersion)
        };

    public static TeamLabExecutionTopology DecodeExecution(int schemaVersion, string canonicalJson)
    {
        return schemaVersion switch
        {
            1 => CompileWithDigests(
                TeamLabTopologyV1Normalizer.Normalize(DecodeDefinition(1, canonicalJson)),
                JsonSerializer.Deserialize<TeamLabTopologyDefinitionV1Model>(canonicalJson, JsonOptions)
                    ?.Assets.ToDictionary(item => item.Key, item => item.ImageDigest, StringComparer.Ordinal)
                ?? throw InvalidRelease()),
            2 => CompileWithDigests(
                TeamLabTopologyV2Compiler.Compile(DecodeDefinition(2, canonicalJson)),
                JsonSerializer.Deserialize<TeamLabTopologyDefinitionV2Model>(canonicalJson, JsonOptions)
                    ?.Assets.ToDictionary(item => item.Key, item => item.ImageDigest, StringComparer.Ordinal)
                ?? throw InvalidRelease()),
            _ => throw UnsupportedSchema(schemaVersion)
        };
    }

    public static string ComputeContentHash(int schemaVersion, string canonicalJson)
    {
        using var document = JsonDocument.Parse(canonicalJson);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new { schemaVersion, topology = document.RootElement }, JsonOptions);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(payload))}";
    }

    public static TeamLabTopologyDefinitionModel Normalize(TeamLabTopologyDefinitionModel definition) =>
        new(
            definition.Name.Trim(),
            definition.Networks.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item with
            {
                Key = item.Key.Trim(),
                Name = item.Name.Trim(),
                AddressPool = new TeamLabAddressPoolModel(item.AddressPool.PoolCidr.Trim(), item.AddressPool.RuntimePrefixLength)
            }).ToArray(),
            definition.Assets.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item with
            {
                Key = item.Key.Trim(),
                Name = item.Name.Trim(),
                Interfaces = item.Interfaces.OrderBy(iface => iface.Key, StringComparer.Ordinal).Select(iface => iface with
                {
                    Key = iface.Key.Trim(),
                    NetworkKey = iface.NetworkKey.Trim()
                }).ToArray()
            }).ToArray(),
            definition.Connections.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item with
            {
                Key = item.Key.Trim(),
                FromNetworkKey = item.FromNetworkKey.Trim(),
                ToNetworkKey = item.ToNetworkKey.Trim(),
                ViaAssetKey = TrimToNull(item.ViaAssetKey),
                ViaNodeKey = TrimToNull(item.ViaNodeKey)
            }).ToArray(),
            definition.Infrastructure?.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item with
            {
                Key = item.Key.Trim(), Name = item.Name.Trim(), NetworkKey = TrimToNull(item.NetworkKey),
                Interfaces = item.Interfaces.OrderBy(iface => iface.Key, StringComparer.Ordinal).Select(iface => iface with
                {
                    Key = iface.Key.Trim(), NetworkKey = iface.NetworkKey.Trim()
                }).ToArray()
            }).ToArray(),
            definition.Dependencies?.OrderBy(item => item.AssetKey, StringComparer.Ordinal)
                .ThenBy(item => item.DependsOnKey, StringComparer.Ordinal).ThenBy(item => item.Condition)
                .Select(item => item with { AssetKey = item.AssetKey.Trim(), DependsOnKey = item.DependsOnKey.Trim() }).ToArray(),
            definition.Observation);

    private static TeamLabExecutionTopology CompileWithDigests(
        TeamLabExecutionTopology topology,
        IReadOnlyDictionary<string, string?> digests) => topology with
    {
        Assets = topology.Assets.Select(asset => asset with { ImageDigest = digests.GetValueOrDefault(asset.Key) }).ToArray()
    };

    private static TeamLabTopologyDefinitionV1Model ToV1(
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyDictionary<string, string>? imageDigests) => new(
        definition.Name,
        definition.Networks,
        definition.Assets.Select(asset => new TeamLabTopologyAssetV1Model(
            asset.Key, asset.Name, asset.Kind, asset.ImageTemplateId, asset.Resources, asset.Interfaces,
            asset.ExposePort, asset.HealthCheck, asset.OrderIndex, imageDigests?.GetValueOrDefault(asset.Key))).ToArray(),
        definition.Connections.Select(connection => new TeamLabTopologyConnectionV1Model(
            connection.Key, connection.FromNetworkKey, connection.ToNetworkKey, connection.ViaAssetKey ?? string.Empty)).ToArray());

    private static TeamLabTopologyDefinitionV2Model ToV2(
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyDictionary<string, string>? imageDigests)
    {
        var execution = TeamLabTopologyV2Compiler.Compile(definition);
        return new TeamLabTopologyDefinitionV2Model(
            execution.Name,
            execution.Networks.Select(network => new TeamLabTopologyNetworkModel(
                network.Key, network.Name, new TeamLabAddressPoolModel(network.AddressPoolCidr, network.RuntimePrefixLength),
                network.IsEntry, network.DisplayOrder)).ToArray(),
            execution.Infrastructure.Select(item => new TeamLabTopologyInfrastructureModel(
                item.Key, item.Name, item.Kind, item.Interfaces.Select(ToContract).ToArray(), item.NetworkKey)).ToArray(),
            execution.Assets.Select(asset => new TeamLabTopologyAssetV2Model(
                asset.Key, asset.Name, asset.Kind, asset.ImageTemplateId,
                new TeamLabAssetResourceModel(asset.CpuUnits, asset.MemoryMiB, asset.StorageMiB),
                asset.Interfaces.Select(ToContract).ToArray(), asset.EndpointObservation, asset.ExposePort,
                asset.HealthCheckKind is { } kind && asset.HealthCheckPort is { } port
                    ? new TeamLabHealthCheckModel(kind, port)
                    : null,
                asset.DisplayOrder, imageDigests?.GetValueOrDefault(asset.Key))).ToArray(),
            execution.Connections.Select(connection => new TeamLabTopologyConnectionV2Model(
                connection.Key, connection.FromNetworkKey, connection.ToNetworkKey, connection.ViaNodeKey,
                connection.ViaAssetKey, connection.Direction)).ToArray(),
            execution.Dependencies.Select(dependency => new TeamLabTopologyDependencyModel(
                dependency.AssetKey, dependency.DependsOnKey, dependency.Condition)).ToArray(),
            new TeamLabObservationPolicyModel(execution.Observation.FlowMetadataEnabled,
                execution.Observation.OnDemandPcapEnabled, execution.Observation.EndpointObservation));
    }

    private static TeamLabTopologyDefinitionModel FromV1(TeamLabTopologyDefinitionV1Model definition) => Normalize(new(
        definition.Name,
        definition.Networks,
        definition.Assets.Select(asset => new TeamLabTopologyAssetModel(
            asset.Key, asset.Name, asset.Kind, asset.ImageTemplateId, asset.Resources, asset.Interfaces,
            asset.ExposePort, asset.HealthCheck, asset.OrderIndex)).ToArray(),
        definition.Connections.Select(connection => new TeamLabTopologyConnectionModel(
            connection.Key, connection.FromNetworkKey, connection.ToNetworkKey, connection.ViaAssetKey)).ToArray()));

    private static TeamLabTopologyDefinitionModel FromV2(TeamLabTopologyDefinitionV2Model definition) => Normalize(new(
        definition.Name,
        definition.Networks,
        definition.Assets.Select(asset => new TeamLabTopologyAssetModel(
            asset.Key, asset.Name, asset.Kind, asset.ImageTemplateId, asset.Resources, asset.Interfaces,
            asset.ExposePort, asset.HealthCheck, asset.OrderIndex, asset.EndpointObservation)).ToArray(),
        definition.Connections.Select(connection => new TeamLabTopologyConnectionModel(
            connection.Key, connection.FromNetworkKey, connection.ToNetworkKey, connection.ViaAssetKey,
            connection.ViaNodeKey, connection.Direction)).ToArray(),
        definition.Infrastructure, definition.Dependencies, definition.Observation));

    private static TeamLabTopologyInterfaceModel ToContract(TeamLabExecutionInterface iface) =>
        new(iface.Key, iface.NetworkKey, iface.HostOffset, iface.Primary, iface.DisplayOrder);

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TeamLabApiContractException InvalidRelease() =>
        new("release_invalid", "The topology release payload is invalid.", 500);

    private static TeamLabApiContractException UnsupportedSchema(int schemaVersion) =>
        new("topology_schema_unsupported", $"Topology schema version {schemaVersion} is not supported.", 422);

    private sealed record TeamLabTopologyDefinitionV1Model(
        string Name, IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
        IReadOnlyList<TeamLabTopologyAssetV1Model> Assets,
        IReadOnlyList<TeamLabTopologyConnectionV1Model> Connections);

    private sealed record TeamLabTopologyAssetV1Model(
        string Key, string Name, TeamLabAssetKind Kind, int ImageTemplateId,
        TeamLabAssetResourceModel Resources, IReadOnlyList<TeamLabTopologyInterfaceModel> Interfaces,
        int? ExposePort, TeamLabHealthCheckModel? HealthCheck, int OrderIndex, string? ImageDigest = null);

    private sealed record TeamLabTopologyConnectionV1Model(
        string Key, string FromNetworkKey, string ToNetworkKey, string ViaAssetKey);
}
