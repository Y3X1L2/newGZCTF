using System.ComponentModel.DataAnnotations;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabConnectorKind : byte
{
    ManagedNic = 1,
    Vlan = 2,
    Segment = 3,
    Serial = 4,
    UsbGateway = 5,
    DedicatedNetwork = 6
}

public enum TeamLabConnectorHealth : byte
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unreachable = 3
}

public enum TeamLabConnectorLeaseReleaseReason : byte
{
    None = 0,
    ManualRelease = 1,
    RuntimeDestroyed = 2,
    AdminRevoked = 3,
    NodeLost = 4
}

/// <summary>
/// Administered piece of real infrastructure a scenario may attach to. The
/// attachment endpoint is operational data and never leaves the platform:
/// scenarios, releases and external callers reference the connector by id only.
/// </summary>
public sealed class TeamLabConnector
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    [MaxLength(96)] public string Name { get; set; } = string.Empty;
    [MaxLength(128)] public string DisplayName { get; set; } = string.Empty;
    public TeamLabConnectorKind Kind { get; set; }
    /// <summary>Authorization boundary; null means a platform-wide resource.</summary>
    public Guid? ControlScopeId { get; set; }
    /// <summary>Explicit opt-in for concurrent use; connectors are exclusive by default.</summary>
    public bool SupportsSharedUse { get; set; }
    public int Capacity { get; set; } = 1;
    [MaxLength(512)] public string? AttachmentReference { get; set; }
    [MaxLength(2048)] public string? Description { get; set; }
    public TeamLabConnectorHealth Health { get; set; } = TeamLabConnectorHealth.Unknown;
    public DateTimeOffset? HealthObservedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabControlScope? ControlScope { get; set; }
    public List<TeamLabConnectorLease> Leases { get; set; } = [];
}

/// <summary>
/// Occupancy fact tying a connector to one runtime. Active leases are bounded
/// by the connector capacity through a filtered unique index on
/// (ConnectorId, Slot), so exclusivity survives crashes and races.
/// </summary>
public sealed class TeamLabConnectorLease
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int ConnectorId { get; set; }
    public int RuntimeId { get; set; }
    public int Slot { get; set; }
    public DateTimeOffset AcquiredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReleasedAt { get; set; }
    public TeamLabConnectorLeaseReleaseReason ReleaseReason { get; set; } = TeamLabConnectorLeaseReleaseReason.None;
    public TeamLabConnector Connector { get; set; } = null!;
    public TeamLabRuntime Runtime { get; set; } = null!;
}
