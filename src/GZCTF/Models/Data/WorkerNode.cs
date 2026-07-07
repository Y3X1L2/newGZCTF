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
    public int ReservedContainers { get; set; }
    public int MaxContainers { get; set; } = 20;
    public int CurrentVms { get; set; }
    public int ReservedVms { get; set; }
    public int MaxVms { get; set; } = 5;
    public int UsedPorts { get; set; }
    public int TotalPorts { get; set; } = 28231;
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

    [NotMapped]
    public int AllocatedContainers => Math.Max(0, CurrentContainers) + Math.Max(0, ReservedContainers);

    [NotMapped]
    public int AllocatedVms => Math.Max(0, CurrentVms) + Math.Max(0, ReservedVms);
}

[Flags]
public enum NodeCapability : byte { None = 0, Docker = 1, Kvm = 2 }
public enum NodeStatus : byte { Unknown = 0, Online = 1, Offline = 2, Busy = 3, Error = 4 }
public enum TeamLabTunnelStatus : byte { Unknown = 0, Disabled = 1, Probing = 2, Healthy = 3, Error = 4 }
