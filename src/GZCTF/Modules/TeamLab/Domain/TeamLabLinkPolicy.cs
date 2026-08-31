using System.ComponentModel.DataAnnotations;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabLinkPolicyKind : byte
{
    AccessRule = 1,
    Nat = 2,
    BandwidthLimit = 3,
    Latency = 4,
    Jitter = 5,
    PacketLoss = 6,
    Duplication = 7,
    LinkBreak = 8
}

public enum TeamLabLinkPolicyStatus : byte
{
    Active = 1,
    Recovered = 2,
    Failed = 3
}

public enum TeamLabLinkPolicyRecoverOrigin : byte
{
    None = 0,
    Scheduled = 1,
    Manual = 2,
    RuntimeDestroyed = 3
}

/// <summary>
/// Desired network/link impairment or access policy for one runtime link.
/// The policy belongs to the network/link object, never to guest scripts;
/// the execution plane converges the desired state and the entity keeps the
/// audit trail (applied, scheduled or manual recovery, failure reason).
/// </summary>
public sealed class TeamLabLinkPolicy
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public Guid? ControlScopeId { get; set; }
    [MaxLength(64)] public string NetworkKey { get; set; } = string.Empty;
    [MaxLength(64)] public string? AssetKey { get; set; }
    public TeamLabLinkPolicyKind Kind { get; set; }
    /// <summary>Canonical JSON parameters validated per kind.</summary>
    [MaxLength(1024)] public string ParametersJson { get; set; } = "{}";
    public TeamLabLinkPolicyStatus Status { get; set; } = TeamLabLinkPolicyStatus.Active;
    public DateTimeOffset? RecoverAt { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RecoveredAt { get; set; }
    public TeamLabLinkPolicyRecoverOrigin RecoverOrigin { get; set; } = TeamLabLinkPolicyRecoverOrigin.None;
    [MaxLength(512)] public string? LastError { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime Runtime { get; set; } = null!;
}
