using System.ComponentModel.DataAnnotations;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabPathConfidence : byte
{
    PacketExact = 0,
    ProcessCorrelated = 1,
    TemporallyRelated = 2
}

public sealed class TeamLabTrafficPath
{
    [Key] public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public TeamLabPathConfidence Confidence { get; set; }
    public byte[] EvidenceFingerprint { get; set; } = [];
    [MaxLength(64)] public string SourceIp { get; set; } = string.Empty;
    public int? SourcePort { get; set; }
    [MaxLength(64)] public string DestinationIp { get; set; } = string.Empty;
    public int? DestinationPort { get; set; }
    [MaxLength(16)] public string Protocol { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime Runtime { get; set; } = null!;
    public List<TeamLabTrafficPathHop> Hops { get; set; } = [];
}

public sealed class TeamLabTrafficPathHop
{
    [Key] public long Id { get; set; }
    public long PathId { get; set; }
    public int Ordinal { get; set; }
    public long? ObservationId { get; set; }
    public int ObservationPointId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public TeamLabTrafficEvidenceKind EvidenceKind { get; set; }
    [MaxLength(16)] public string Direction { get; set; } = "observed";
    [MaxLength(64)] public string SourceIp { get; set; } = string.Empty;
    public int? SourcePort { get; set; }
    [MaxLength(64)] public string DestinationIp { get; set; } = string.Empty;
    public int? DestinationPort { get; set; }
    [MaxLength(16)] public string Protocol { get; set; } = string.Empty;
    public TeamLabTrafficPath Path { get; set; } = null!;
    public TeamLabTrafficObservation? Observation { get; set; }
    public TeamLabObservationPoint ObservationPoint { get; set; } = null!;
}

public sealed class TeamLabTrafficCorrelationCursor
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public long LastObservationId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime Runtime { get; set; } = null!;
}
