using System.Text.Json;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabTopologyInfrastructureModel(
    string Key,
    string Name,
    TeamLabInfrastructureKind Kind,
    IReadOnlyList<TeamLabTopologyInterfaceModel> Interfaces,
    string? NetworkKey = null);

public sealed record TeamLabTopologyDependencyModel(
    string AssetKey,
    string DependsOnKey,
    TeamLabDependencyCondition Condition);

public sealed record TeamLabObservationPolicyModel(
    bool FlowMetadataEnabled = true,
    bool OnDemandPcapEnabled = true,
    TeamLabEndpointObservationMode EndpointObservation = TeamLabEndpointObservationMode.Optional);

internal sealed record TeamLabTopologyAssetV2Model(
    string Key,
    string Name,
    TeamLabAssetKind Kind,
    int ImageTemplateId,
    TeamLabAssetResourceModel Resources,
    IReadOnlyList<TeamLabTopologyInterfaceModel> Interfaces,
    TeamLabEndpointObservationMode EndpointObservation,
    int? ExposePort,
    TeamLabHealthCheckModel? HealthCheck,
    int OrderIndex,
    string? ImageDigest = null,
    int? DevicePackageId = null,
    JsonElement? DeviceParameters = null,
    Guid? ConnectorId = null,
    string? DevicePackageDigest = null);

internal sealed record TeamLabTopologyConnectionV2Model(
    string Key,
    string FromNetworkKey,
    string ToNetworkKey,
    string? ViaNodeKey,
    string? ViaAssetKey,
    TeamLabConnectionDirection Direction);

internal sealed record TeamLabTopologyDefinitionV2Model(
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyInfrastructureModel> Infrastructure,
    IReadOnlyList<TeamLabTopologyAssetV2Model> Assets,
    IReadOnlyList<TeamLabTopologyConnectionV2Model> Connections,
    IReadOnlyList<TeamLabTopologyDependencyModel> Dependencies,
    TeamLabObservationPolicyModel Observation);
