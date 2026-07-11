using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabAddressPoolModel(string PoolCidr, int RuntimePrefixLength);

public sealed record TeamLabTopologyNetworkModel(
    string Key,
    string Name,
    TeamLabAddressPoolModel AddressPool,
    bool IsEntry,
    int OrderIndex = 0);

public sealed record TeamLabTopologyInterfaceModel(
    string Key,
    string NetworkKey,
    int HostOffset,
    bool Primary,
    int OrderIndex = 0);

public sealed record TeamLabAssetResourceModel(int CpuUnits, int MemoryMiB, int StorageMiB);

public sealed record TeamLabHealthCheckModel(TeamLabHealthCheckKind Kind, int Port);

public sealed record TeamLabTopologyAssetModel(
    string Key,
    string Name,
    TeamLabAssetKind Kind,
    int ImageTemplateId,
    TeamLabAssetResourceModel Resources,
    IReadOnlyList<TeamLabTopologyInterfaceModel> Interfaces,
    bool RoutingEnabled,
    int? ExposePort = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StartCommand = null,
    TeamLabHealthCheckModel? HealthCheck = null,
    int OrderIndex = 0);

public sealed record TeamLabTopologyConnectionModel(
    string Key,
    string FromNetworkKey,
    string ToNetworkKey,
    string ViaAssetKey);

public sealed record TeamLabTopologyDefinitionModel(
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections);

public sealed record CreateTeamLabTopologyModel(
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections);

public sealed record UpdateTeamLabTopologyModel(
    int Revision,
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections);

public sealed record TeamLabTopologySummaryModel(
    Guid Id,
    string Name,
    int Revision,
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamLabTopologyDetailModel(
    Guid Id,
    int Revision,
    int SchemaVersion,
    TeamLabTopologyDefinitionModel Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamLabValidationIssueModel(string Code, string Path, string Message);

public sealed record TeamLabValidationResultModel(bool Valid, IReadOnlyList<TeamLabValidationIssueModel> Issues);

public sealed record TeamLabReleaseModel(
    Guid Id,
    Guid TopologyId,
    int Version,
    int SourceRevision,
    int SchemaVersion,
    string ContentHash,
    Guid? PublishedBy,
    DateTimeOffset PublishedAt);

public sealed record TeamLabCapabilitiesModel(
    string ApiVersion,
    IReadOnlyList<int> TopologySchemaVersions,
    IReadOnlyList<TeamLabAssetKind> AssetKinds,
    string NetworkModel,
    TeamLabFeatureCapabilitiesModel Features,
    TeamLabContractLimitsModel Limits);

public sealed record TeamLabFeatureCapabilitiesModel(
    bool MultiNode,
    bool LinuxVm,
    bool WindowsVm,
    bool TrafficFlows,
    bool OnDemandPcap);

public sealed record TeamLabContractLimitsModel(
    int NetworksPerTopology,
    int AssetsPerTopology,
    int InterfacesPerAsset);
