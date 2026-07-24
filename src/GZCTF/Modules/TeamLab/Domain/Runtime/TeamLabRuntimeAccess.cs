using System.ComponentModel.DataAnnotations;

namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public class TeamLabVpnPeerRuntime
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    [MaxLength(64)] public string ClientAddress { get; set; } = string.Empty;
    [MaxLength(256)] public string Endpoint { get; set; } = string.Empty;
    [MaxLength(256)] public string AllowedIPs { get; set; } = string.Empty;
    [MaxLength(64)] public string Dns { get; set; } = string.Empty;
    [MaxLength(128)] public string PublicKey { get; set; } = string.Empty;
    [MaxLength(1024)] public string ProtectedClientPrivateKey { get; set; } = string.Empty;
    [MaxLength(128)] public string ServerPublicKey { get; set; } = string.Empty;
    [MaxLength(1024)] public string ProtectedServerPrivateKey { get; set; } = string.Empty;
    public int ConfigVersion { get; set; } = 1;
    public bool Revoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime Runtime { get; set; } = null!;
}

public class TeamLabAccessGrant
{
    [Key] public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public Guid? ApiOperationId { get; set; }
    public TeamLabAccessGrantType Type { get; set; } = TeamLabAccessGrantType.WireGuard;
    [MaxLength(64)] public string ClientAddress { get; set; } = string.Empty;
    [MaxLength(256)] public string Endpoint { get; set; } = string.Empty;
    [MaxLength(512)] public string AllowedIps { get; set; } = string.Empty;
    [MaxLength(64)] public string Dns { get; set; } = string.Empty;
    [MaxLength(128)] public string PublicKey { get; set; } = string.Empty;
    [MaxLength(1024)] public string ProtectedPrivateKey { get; set; } = string.Empty;
    [MaxLength(128)] public string ServerPublicKey { get; set; } = string.Empty;
    [MaxLength(1024)] public string ProtectedServerPrivateKey { get; set; } = string.Empty;
    [MaxLength(128)] public string DownloadTokenHash { get; set; } = string.Empty;
    [MaxLength(1024)] public string? ProtectedDownloadToken { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public bool Revoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? ConfigurationConsumedAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
}

public class TeamLabRuntimeSecretEnvelope
{
    [Key] public long Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public string? ProtectedPayload { get; set; }
    [MaxLength(128)] public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
}

public class TeamLabPublicUdpMapping
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public int PublicUdpPort { get; set; }
    [MaxLength(64)] public string WorkerTunnelIp { get; set; } = string.Empty;
    public int WorkerWireGuardPort { get; set; }
    public int RuleVersion { get; set; }
    public bool IsSynced { get; set; }
    [MaxLength(1024)] public string? LastSyncError { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
}
