using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabDevicePackagePortModel(string Name, int Port, string Protocol);

public sealed record TeamLabDevicePackageModel(
    Guid Id,
    string Name,
    string DisplayName,
    string Version,
    string ArtifactKind,
    string ArtifactReference,
    string? Digest,
    string? Description,
    IReadOnlyList<string> SupportedAssetKinds,
    int CpuMillis,
    int MemoryMib,
    int StorageGib,
    IReadOnlyList<TeamLabDevicePackagePortModel> Ports,
    [property: JsonPropertyName("parameterSchema")] JsonElement? ParameterSchema,
    [property: JsonPropertyName("healthDeclaration")] JsonElement? HealthDeclaration,
    IReadOnlyList<string> ProtocolEventTypes,
    bool Enabled,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamLabDevicePackagePageModel(
    IReadOnlyList<TeamLabDevicePackageModel> Items,
    string? Next);

public sealed record RegisterTeamLabDevicePackageModel(
    string Name,
    string DisplayName,
    string Version,
    string ArtifactKind,
    string ArtifactReference,
    string? Digest,
    string? Description,
    IReadOnlyList<string>? SupportedAssetKinds,
    int CpuMillis,
    int MemoryMib,
    int StorageGib,
    IReadOnlyList<TeamLabDevicePackagePortModel>? Ports,
    [property: JsonPropertyName("parameterSchema")] JsonElement? ParameterSchema,
    [property: JsonPropertyName("healthDeclaration")] JsonElement? HealthDeclaration,
    IReadOnlyList<string>? ProtocolEventTypes);

public sealed record TeamLabConnectorLeaseModel(
    Guid Id,
    Guid ConnectorId,
    Guid RuntimeId,
    int Slot,
    DateTimeOffset AcquiredAt,
    DateTimeOffset? ReleasedAt,
    string ReleaseReason);

public sealed record TeamLabConnectorModel(
    Guid Id,
    string Name,
    string DisplayName,
    string Kind,
    Guid? ControlScopeId,
    bool SupportsSharedUse,
    int Capacity,
    int OccupiedSlots,
    IReadOnlyList<TeamLabConnectorLeaseModel> ActiveLeases,
    string Health,
    DateTimeOffset? HealthObservedAt,
    string? Description,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamLabConnectorPageModel(
    IReadOnlyList<TeamLabConnectorModel> Items,
    string? Next);

public sealed record RegisterTeamLabConnectorModel(
    string Name,
    string DisplayName,
    string Kind,
    Guid? ControlScopeId,
    bool SupportsSharedUse,
    int Capacity,
    string? AttachmentReference,
    string? Description);

public sealed record SetTeamLabConnectorHealthModel(string Health);

public sealed record AcquireTeamLabConnectorLeaseModel(Guid RuntimeId);

public sealed record ReleaseTeamLabConnectorLeaseModel(Guid RuntimeId);

public sealed record TeamLabLinkPolicyModel(
    Guid Id,
    Guid RuntimeId,
    string NetworkKey,
    string? AssetKey,
    string Kind,
    [property: JsonPropertyName("parameters")] JsonElement? Parameters,
    string Status,
    DateTimeOffset? RecoverAt,
    DateTimeOffset AppliedAt,
    DateTimeOffset? RecoveredAt,
    string RecoverOrigin,
    string? LastError);

public sealed record TeamLabLinkPolicyPageModel(
    IReadOnlyList<TeamLabLinkPolicyModel> Items,
    string? Next);

public sealed record ApplyTeamLabLinkPolicyModel(
    Guid RuntimeId,
    string NetworkKey,
    string? AssetKey,
    string Kind,
    [property: JsonPropertyName("parameters")] JsonElement? Parameters,
    DateTimeOffset? RecoverAt);

public sealed record TeamLabComputeNodePoolModel(
    Guid Id,
    string Name,
    string Status,
    bool Schedulable,
    bool DockerCapable,
    bool KvmCapable,
    bool TeamLabNetworkEnabled,
    string FabricStatus,
    int CurrentContainers,
    int MaxContainers,
    int CurrentVms,
    int MaxVms,
    double CpuLoadPercent,
    double MemoryLoadPercent,
    string? AgentVersion,
    DateTimeOffset? LastHeartbeat,
    DateTimeOffset? MetricObservedAt);

public sealed record TeamLabTemplatePoolModel(
    int Id,
    string Name,
    string OsType,
    string ImageType,
    string Status,
    long FileSizeBytes,
    string? Digest,
    bool SupportsInstanceCredentials,
    DateTimeOffset UploadedAt);

public sealed record TeamLabNodeCachePoolModel(
    int TemplateId,
    Guid NodeId,
    string? ImageHash,
    string Status,
    string Operation,
    string Stage,
    int AttemptCount,
    int ActiveReferenceCount,
    string? LastErrorCode,
    DateTimeOffset? ProgressUpdatedAt);

public sealed record TeamLabResourcePoolSnapshotModel(
    IReadOnlyList<TeamLabComputeNodePoolModel> ComputeNodes,
    IReadOnlyList<TeamLabTemplatePoolModel> Templates);

public sealed record TeamLabNodeCachePageModel(
    IReadOnlyList<TeamLabNodeCachePoolModel> Items,
    string? Next);

public static class TeamLabCapabilityResourceContractMapper
{
    public static bool TryParseArtifactKind(string? value, out TeamLabDevicePackageArtifactKind kind) =>
        EnumTryParse(value, out kind,
            (TeamLabDevicePackageArtifactKind.OciImage, "oci-image"),
            (TeamLabDevicePackageArtifactKind.VmImage, "vm-image"));

    public static bool TryParseConnectorKind(string? value, out TeamLabConnectorKind kind) =>
        EnumTryParse(value, out kind,
            (TeamLabConnectorKind.ManagedNic, "managed-nic"),
            (TeamLabConnectorKind.Vlan, "vlan"),
            (TeamLabConnectorKind.Segment, "segment"),
            (TeamLabConnectorKind.Serial, "serial"),
            (TeamLabConnectorKind.UsbGateway, "usb-gateway"),
            (TeamLabConnectorKind.DedicatedNetwork, "dedicated-network"));

    public static bool TryParseConnectorHealth(string? value, out TeamLabConnectorHealth health) =>
        EnumTryParse(value, out health,
            (TeamLabConnectorHealth.Unknown, "unknown"),
            (TeamLabConnectorHealth.Healthy, "healthy"),
            (TeamLabConnectorHealth.Degraded, "degraded"),
            (TeamLabConnectorHealth.Unreachable, "unreachable"));

    public static bool TryParseLinkPolicyKind(string? value, out TeamLabLinkPolicyKind kind) =>
        EnumTryParse(value, out kind,
            (TeamLabLinkPolicyKind.AccessRule, "access-rule"),
            (TeamLabLinkPolicyKind.Nat, "nat"),
            (TeamLabLinkPolicyKind.BandwidthLimit, "bandwidth-limit"),
            (TeamLabLinkPolicyKind.Latency, "latency"),
            (TeamLabLinkPolicyKind.Jitter, "jitter"),
            (TeamLabLinkPolicyKind.PacketLoss, "packet-loss"),
            (TeamLabLinkPolicyKind.Duplication, "duplication"),
            (TeamLabLinkPolicyKind.LinkBreak, "link-break"));

    private static bool EnumTryParse<T>(string? value, out T parsed, params (T Value, string Name)[] names)
        where T : struct, Enum
    {
        foreach (var (candidate, name) in names)
            if (string.Equals(name, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                parsed = candidate;
                return true;
            }
        parsed = default;
        return false;
    }

    public static string ArtifactKindName(TeamLabDevicePackageArtifactKind kind) => kind switch
    {
        TeamLabDevicePackageArtifactKind.OciImage => "oci-image",
        TeamLabDevicePackageArtifactKind.VmImage => "vm-image",
        _ => kind.ToString()
    };

    public static string ConnectorKindName(TeamLabConnectorKind kind) => kind switch
    {
        TeamLabConnectorKind.ManagedNic => "managed-nic",
        TeamLabConnectorKind.Vlan => "vlan",
        TeamLabConnectorKind.Segment => "segment",
        TeamLabConnectorKind.Serial => "serial",
        TeamLabConnectorKind.UsbGateway => "usb-gateway",
        TeamLabConnectorKind.DedicatedNetwork => "dedicated-network",
        _ => kind.ToString()
    };

    public static string ConnectorHealthName(TeamLabConnectorHealth health) => health switch
    {
        TeamLabConnectorHealth.Unknown => "unknown",
        TeamLabConnectorHealth.Healthy => "healthy",
        TeamLabConnectorHealth.Degraded => "degraded",
        TeamLabConnectorHealth.Unreachable => "unreachable",
        _ => health.ToString()
    };

    public static string LeaseReleaseReasonName(TeamLabConnectorLeaseReleaseReason reason) => reason switch
    {
        TeamLabConnectorLeaseReleaseReason.None => "none",
        TeamLabConnectorLeaseReleaseReason.ManualRelease => "manual-release",
        TeamLabConnectorLeaseReleaseReason.RuntimeDestroyed => "runtime-destroyed",
        TeamLabConnectorLeaseReleaseReason.AdminRevoked => "admin-revoked",
        TeamLabConnectorLeaseReleaseReason.NodeLost => "node-lost",
        _ => reason.ToString()
    };

    public static string LinkPolicyKindName(TeamLabLinkPolicyKind kind) => kind switch
    {
        TeamLabLinkPolicyKind.AccessRule => "access-rule",
        TeamLabLinkPolicyKind.Nat => "nat",
        TeamLabLinkPolicyKind.BandwidthLimit => "bandwidth-limit",
        TeamLabLinkPolicyKind.Latency => "latency",
        TeamLabLinkPolicyKind.Jitter => "jitter",
        TeamLabLinkPolicyKind.PacketLoss => "packet-loss",
        TeamLabLinkPolicyKind.Duplication => "duplication",
        TeamLabLinkPolicyKind.LinkBreak => "link-break",
        _ => kind.ToString()
    };

    public static string LinkPolicyStatusName(TeamLabLinkPolicyStatus status) => status switch
    {
        TeamLabLinkPolicyStatus.Active => "active",
        TeamLabLinkPolicyStatus.Recovered => "recovered",
        TeamLabLinkPolicyStatus.Failed => "failed",
        _ => status.ToString()
    };

    public static string LinkPolicyRecoverOriginName(TeamLabLinkPolicyRecoverOrigin origin) => origin switch
    {
        TeamLabLinkPolicyRecoverOrigin.None => "none",
        TeamLabLinkPolicyRecoverOrigin.Scheduled => "scheduled",
        TeamLabLinkPolicyRecoverOrigin.Manual => "manual",
        TeamLabLinkPolicyRecoverOrigin.RuntimeDestroyed => "runtime-destroyed",
        _ => origin.ToString()
    };
}
