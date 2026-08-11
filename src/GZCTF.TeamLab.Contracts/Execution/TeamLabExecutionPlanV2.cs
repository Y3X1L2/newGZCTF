namespace GZCTF.TeamLab.Contracts.Execution;

public sealed record TeamLabExecutionPlanV2(
    int RuntimeId,
    Guid RuntimePublicId,
    int Generation,
    string ShardKey,
    string PlanDigest,
    IReadOnlyList<TeamLabNetworkIntentV2> Networks,
    IReadOnlyList<TeamLabAssetExecutionSpecV2> Assets,
    IReadOnlyList<TeamLabArtifactReferenceV2> Artifacts,
    IReadOnlyList<TeamLabObservationIntentV2> ObservationPoints)
{
    public bool IsValid(out string? error)
    {
        if (RuntimeId <= 0 || RuntimePublicId == Guid.Empty || Generation <= 0)
        {
            error = "Runtime identity is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ShardKey) || string.IsNullOrWhiteSpace(PlanDigest))
        {
            error = "Shard key and plan digest are required.";
            return false;
        }

        if (Assets.Any(item => string.IsNullOrWhiteSpace(item.AssetKey) ||
                               string.IsNullOrWhiteSpace(item.ResourceId)))
        {
            error = "Every asset requires an asset key and resource identity.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed record TeamLabNetworkIntentV2(
    string Key,
    string Kind,
    string Cidr,
    string? GatewayIp,
    IReadOnlyList<TeamLabNetworkPortV2> Ports,
    IReadOnlyList<TeamLabNetworkRouteV2> Routes,
    IReadOnlyList<TeamLabNetworkPolicyV2> Policies);

public sealed record TeamLabNetworkPortV2(
    string Key,
    string AssetKey,
    string MacAddress,
    string? IpAddress,
    bool IsPrimary);

public sealed record TeamLabNetworkRouteV2(
    string DestinationCidr,
    string? NextHop,
    string PortKey);

public sealed record TeamLabNetworkPolicyV2(
    string SourceCidr,
    string DestinationCidr,
    string Protocol,
    int? Port,
    bool Allow);

public sealed record TeamLabAssetExecutionSpecV2(
    string AssetKey,
    string Kind,
    string ResourceId,
    string ImageDigest,
    string? DomainIdentity,
    int Cpu,
    int MemoryMiB,
    IReadOnlyList<TeamLabAssetNetworkAttachmentV2> NetworkAttachments,
    IReadOnlyList<TeamLabHealthCheckV2> HealthChecks);

public sealed record TeamLabAssetNetworkAttachmentV2(
    string NetworkKey,
    string PortKey,
    string InterfaceName,
    string? IpAddress,
    bool IsPrimary);

public sealed record TeamLabHealthCheckV2(
    string Protocol,
    string Host,
    int Port,
    string? Path);

public sealed record TeamLabArtifactReferenceV2(
    string AssetKey,
    string Digest,
    string ArtifactType,
    long SizeBytes);

public sealed record TeamLabObservationIntentV2(
    Guid ObservationPointId,
    string AssetKey,
    string InterfaceToken,
    bool CaptureMetadata,
    bool CapturePackets);
