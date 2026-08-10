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
    int OrderIndex = 0,
    bool Stateless = false,
    TeamLabBootstrapReferenceModel? Bootstrap = null,
    TeamLabEndpointObservationMode EndpointObservation = TeamLabEndpointObservationMode.Disabled,
    bool BakeAtPublish = false,
    string? ImageDigest = null);

public sealed record TeamLabTopologyConnectionModel(
    string Key,
    string FromNetworkKey,
    string ToNetworkKey,
    string? ViaAssetKey = null,
    string? ViaNodeKey = null,
    TeamLabConnectionDirection? Direction = null);

public sealed record TeamLabTopologyDefinitionModel(
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections,
    IReadOnlyList<TeamLabTopologyInfrastructureModel>? Infrastructure = null,
    IReadOnlyList<TeamLabTopologyDependencyModel>? Dependencies = null,
    TeamLabObservationPolicyModel? Observation = null);

public sealed record TeamLabEditorItemModel(
    double X,
    double Y,
    double? Width = null,
    double? Height = null,
    bool Collapsed = false);

public sealed record TeamLabTopologyEditorModel(
    IReadOnlyDictionary<string, TeamLabEditorItemModel> Networks,
    IReadOnlyDictionary<string, TeamLabEditorItemModel> Assets,
    IReadOnlyDictionary<string, TeamLabEditorItemModel>? Infrastructure = null);

public sealed record CreateTeamLabTopologyModel(
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections,
    TeamLabTopologyEditorModel? Editor = null,
    IReadOnlyList<TeamLabTopologyInfrastructureModel>? Infrastructure = null,
    IReadOnlyList<TeamLabTopologyDependencyModel>? Dependencies = null,
    TeamLabObservationPolicyModel? Observation = null,
    int SchemaVersion = 2,
    Guid? ControlScopeId = null);

public sealed record UpdateTeamLabTopologyModel(
    int Revision,
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyAssetModel> Assets,
    IReadOnlyList<TeamLabTopologyConnectionModel> Connections,
    TeamLabTopologyEditorModel? Editor = null,
    IReadOnlyList<TeamLabTopologyInfrastructureModel>? Infrastructure = null,
    IReadOnlyList<TeamLabTopologyDependencyModel>? Dependencies = null,
    TeamLabObservationPolicyModel? Observation = null,
    int SchemaVersion = 2);

public sealed record PublishTeamLabTopologyModel(
    int Revision,
    IReadOnlyList<TeamLabRuntimeOverlayModel>? ScenarioOverlays = null);

public sealed record TeamLabTopologySummaryModel(
    Guid Id,
    Guid? ControlScopeId,
    string Name,
    int Revision,
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamLabTopologyDetailModel(
    Guid Id,
    Guid? ControlScopeId,
    int Revision,
    int SchemaVersion,
    TeamLabTopologyDefinitionModel Definition,
    TeamLabTopologyEditorModel Editor,
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
    DateTimeOffset PublishedAt,
    TeamLabTopologyEditorModel? Editor = null);

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
    bool OnDemandPcap,
    bool EditorLayout = true,
    int EditorLayoutVersion = 1,
    bool NetworkRegions = true,
    bool ServiceProfiles = true,
    bool Rollouts = true,
    bool PauseResume = true);

public sealed record TeamLabContractLimitsModel(
    int NetworksPerTopology,
    int AssetsPerTopology,
    int InterfacesPerAsset);
