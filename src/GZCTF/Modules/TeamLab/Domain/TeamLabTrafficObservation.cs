using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabTrafficEvidenceKind : byte
{
    Packet = 0,
    EndpointProcess = 1
}

public sealed class TeamLabTrafficObservation
{
    [Key] public long Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public int ObservationPointId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public long SourceSequence { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    [MaxLength(16)] public string Direction { get; set; } = "observed";
    [MaxLength(64)] public string SourceIp { get; set; } = string.Empty;
    public int? SourcePort { get; set; }
    [MaxLength(64)] public string DestinationIp { get; set; } = string.Empty;
    public int? DestinationPort { get; set; }
    [MaxLength(16)] public string Protocol { get; set; } = string.Empty;
    public byte? TcpFlags { get; set; }
    public int PacketLength { get; set; }
    public byte[]? PacketFingerprint { get; set; }
    public byte[] FlowFingerprint { get; set; } = [];
    public byte[]? ProcessIdentityHash { get; set; }
    public TeamLabTrafficEvidenceKind EvidenceKind { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabObservationPoint ObservationPoint { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
}

public sealed class TeamLabObservationCursor
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public Guid WorkerNodeId { get; set; }
    public long LastSequence { get; set; }
    public long DroppedCount { get; set; }
    public long SensorRejectedCount { get; set; }
    [MaxLength(64)] public string? LastSensorErrorCode { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime Runtime { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
}
