using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabExecutionTopology(
    int SchemaVersion,
    string Name,
    IReadOnlyList<TeamLabExecutionNetwork> Networks,
    IReadOnlyList<TeamLabExecutionInfrastructure> Infrastructure,
    IReadOnlyList<TeamLabExecutionAsset> Assets,
    IReadOnlyList<TeamLabExecutionConnection> Connections,
    IReadOnlyList<TeamLabExecutionDependency> Dependencies,
    TeamLabExecutionObservationPolicy Observation);

public sealed record TeamLabExecutionNetwork(
    string Key,
    string Name,
    string AddressPoolCidr,
    int RuntimePrefixLength,
    bool IsEntry,
    int DisplayOrder);

public sealed record TeamLabExecutionInterface(
    string Key,
    string NetworkKey,
    int HostOffset,
    bool Primary,
    int DisplayOrder);

public sealed record TeamLabExecutionInfrastructure(
    string Key,
    string Name,
    TeamLabInfrastructureKind Kind,
    IReadOnlyList<TeamLabExecutionInterface> Interfaces,
    string? NetworkKey,
    bool Implicit);

public sealed record TeamLabExecutionAsset(
    string Key,
    string Name,
    TeamLabAssetKind Kind,
    int ImageTemplateId,
    int CpuUnits,
    int MemoryMiB,
    int StorageMiB,
    IReadOnlyList<TeamLabExecutionInterface> Interfaces,
    int? ExposePort,
    TeamLabHealthCheckKind? HealthCheckKind,
    int? HealthCheckPort,
    int DisplayOrder,
    TeamLabEndpointObservationMode EndpointObservation,
    string? ImageDigest = null,
    int? DevicePackageId = null,
    string? DeviceParametersJson = null,
    Guid? ConnectorId = null,
    string? DevicePackageDigest = null)
{
    public bool IsImageBacked => true;
}

public sealed record TeamLabExecutionConnection(
    string Key,
    string FromNetworkKey,
    string ToNetworkKey,
    string? ViaNodeKey,
    string? ViaAssetKey,
    TeamLabConnectionDirection Direction);

public sealed record TeamLabExecutionDependency(
    string AssetKey,
    string DependsOnKey,
    TeamLabDependencyCondition Condition);

public sealed record TeamLabExecutionObservationPolicy(
    bool FlowMetadataEnabled,
    bool OnDemandPcapEnabled,
    TeamLabEndpointObservationMode EndpointObservation);

internal sealed record TeamLabRuntimeInfrastructureInterfaceIntent(
    string Key,
    string NetworkKey,
    int HostOffset,
    bool Primary);

internal sealed record TeamLabRuntimeInfrastructureConnectionIntent(
    string FromNetworkKey,
    string ToNetworkKey,
    TeamLabConnectionDirection Direction);
