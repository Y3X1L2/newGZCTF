using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(Status))]
[Index(nameof(LastHeartbeat))]
public class WorkerNode
{
    public static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(120);

    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(128)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(256)] public string HostAddress { get; set; } = string.Empty;
    [Required, MaxLength(128)] public string AuthToken { get; set; } = string.Empty;
    public NodeCapability Capabilities { get; set; } = NodeCapability.Docker;
    public NodeStatus Status { get; set; } = NodeStatus.Unknown;
    public float CpuLoad { get; set; }
    public float MemoryLoad { get; set; }
    public int CurrentContainers { get; set; }
    public int MaxContainers { get; set; } = 20;
    public int CurrentVms { get; set; }
    public int MaxVms { get; set; } = 5;
    public int UsedPorts { get; set; }
    public int TotalPorts { get; set; } = 28231;
    public long LiveMetricSequence { get; set; }
    public DateTimeOffset? LiveMetricObservedAt { get; set; }
    public DateTimeOffset? LiveMetricReceivedAt { get; set; }
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastHeartbeat { get; set; }
    [MaxLength(512)] public string? Labels { get; set; }
    public bool IsSchedulable { get; set; } = true;
    public bool IsLocal { get; set; }
    public bool IsStorageNode { get; set; }
    public int AgentPort { get; set; } = 5001;
    public int RegistryPort { get; set; } = 5000;
    public bool TeamLabNetworkEnabled { get; set; }
    public TeamLabTunnelStatus TeamLabTunnelStatus { get; set; } = TeamLabTunnelStatus.Unknown;
    [MaxLength(64)] public string? TeamLabTunnelIp { get; set; }
    public DateTimeOffset? TeamLabTunnelLastHandshake { get; set; }
    [MaxLength(1024)] public string? TeamLabTunnelLastError { get; set; }
    public int TeamLabTunnelConfigVersion { get; set; }
    [MaxLength(64)] public string? TeamLabFabricIp { get; set; }
    public TeamLabFabricStatus TeamLabFabricStatus { get; set; } = TeamLabFabricStatus.Unknown;
    [MaxLength(64)] public string? AgentVersion { get; set; }
    [MaxLength(128)] public string? AgentBinarySha256 { get; set; }
    public int CapabilityManifestSchemaVersion { get; set; }
    [MaxLength(8192)] public string CapabilityManifestJson { get; set; } = "{}";
    [MaxLength(64)] public string? CapabilityHash { get; set; }
    public DateTimeOffset? CapabilityObservedAt { get; set; }
    public AgentUpdateState AgentUpdateState { get; set; } = AgentUpdateState.Stable;
    public bool AgentUpdateWasSchedulable { get; set; }
    [MaxLength(128)] public string? AgentUpdateExpectedSha256 { get; set; }
    [MaxLength(1024)] public string? AgentUpdateLastError { get; set; }
    public DateTimeOffset? AgentUpdateStartedAt { get; set; }
    public DateTimeOffset? AgentUpdateCompletedAt { get; set; }

    [Timestamp] public uint ConcurrencyToken { get; set; }

    public NodeStatus GetEffectiveStatus(DateTimeOffset utcNow)
    {
        if (Status != NodeStatus.Online || IsLocal)
            return Status;

        if (!LastHeartbeat.HasValue)
            return NodeStatus.Offline;

        return LastHeartbeat.Value < utcNow - DefaultHeartbeatTimeout
            ? NodeStatus.Offline
            : Status;
    }
}

[Flags]
public enum NodeCapability : byte { None = 0, Docker = 1, Kvm = 2 }
public enum NodeStatus : byte { Unknown = 0, Online = 1, Offline = 2, Busy = 3, Error = 4 }
public enum TeamLabTunnelStatus : byte { Unknown = 0, Disabled = 1, Probing = 2, Healthy = 3, Error = 4 }
public enum TeamLabFabricStatus : byte { Unknown = 0, Disabled = 1, Probing = 2, Healthy = 3, Error = 4 }
public enum AgentUpdateState : byte
{
    Stable = 0,
    Cordoned = 1,
    Syncing = 2,
    AwaitingHeartbeat = 3,
    VerifyingFabric = 4,
    Failed = 5
}
