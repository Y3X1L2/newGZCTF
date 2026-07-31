using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public enum TeamLabRemoteProtocol : byte
{
    ContainerTerminal = 1,
    Ssh = 2,
    Rdp = 3
}

public enum TeamLabRemoteSessionStatus : byte
{
    Creating = 1,
    Ready = 2,
    Connected = 3,
    Ending = 4,
    Ended = 5,
    Failed = 6
}

public enum RemoteCredentialMode : byte
{
    PlatformGenerated = 1,
    ExistingAccount = 2
}

[Flags]
public enum TeamLabOperatorPermission : byte
{
    None = 0,
    ViewAssets = 1,
    OperateAssets = 2
}

public sealed class TeamLabRuntimeRemoteCredential
{
    public long Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public int RuntimeAssetId { get; set; }
    public TeamLabRemoteProtocol Protocol { get; set; }
    [MaxLength(128)] public string Username { get; set; } = string.Empty;
    [MaxLength(8192)] public string? ProtectedSecret { get; set; }
    public RemoteCredentialMode Mode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeAsset RuntimeAsset { get; set; } = null!;
}

public sealed class TeamLabRemoteSession
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public int RuntimeAssetId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public TeamLabRemoteProtocol Protocol { get; set; }
    public TeamLabRemoteSessionStatus Status { get; set; } = TeamLabRemoteSessionStatus.Creating;
    [MaxLength(500)] public string Reason { get; set; } = string.Empty;
    [MaxLength(128)] public string? RelayId { get; set; }
    [MaxLength(128)] public string? GuacamoleConnectionId { get; set; }
    [MaxLength(128)] public string? GuacamoleUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    [MaxLength(256)] public string? EndReason { get; set; }
    public Guid CorrelationId { get; set; } = Guid.CreateVersion7();
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeAsset RuntimeAsset { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
    public UserInfo RequestedBy { get; set; } = null!;
    public List<TeamLabRemoteAuditFile> AuditFiles { get; set; } = [];
}

public sealed class TeamLabRemoteAuditFile
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    [MaxLength(512)] public string RelativePath { get; set; } = string.Empty;
    [MaxLength(128)] public string ContentType { get; set; } = string.Empty;
    [MaxLength(64)] public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public TeamLabRemoteSession Session { get; set; } = null!;
}
