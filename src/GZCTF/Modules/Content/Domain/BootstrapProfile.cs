using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.Content.Domain;

public enum BootstrapProfileStatus : byte
{
    Active = 0,
    Deleting = 1,
    Deleted = 2
}

public enum BootstrapProfileVersionStatus : byte
{
    Publishing = 0,
    Ready = 1,
    Error = 2
}

public enum BootstrapProfileOperationAction : byte
{
    Create = 0,
    PublishVersion = 1,
    Delete = 2
}

public enum BootstrapProfileDistributionStatus : byte
{
    Pending = 0,
    Pulling = 1,
    Ready = 2,
    Failed = 3,
    CleanupPending = 4
}

public sealed class BootstrapProfile
{
    [Key] public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    [MaxLength(1024)] public string? Description { get; set; }
    public BootstrapProfileStatus Status { get; set; } = BootstrapProfileStatus.Active;
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public UserInfo CreatedBy { get; set; } = null!;
    public List<BootstrapProfileVersion> Versions { get; set; } = [];
}

public sealed class BootstrapProfileVersion
{
    [Key] public long Id { get; set; }
    public int ProfileId { get; set; }
    public int Version { get; set; }
    public BootstrapProfileVersionStatus Status { get; set; } = BootstrapProfileVersionStatus.Publishing;
    public string ManifestJson { get; set; } = "{}";
    [MaxLength(128)] public string ManifestDigest { get; set; } = string.Empty;
    [MaxLength(256)] public string ManifestSignature { get; set; } = string.Empty;
    public string SigningPublicKeyPem { get; set; } = string.Empty;
    [MaxLength(128)] public string ArtifactDigest { get; set; } = string.Empty;
    public long ArtifactSize { get; set; }
    [MaxLength(256)] public string RegistryAddress { get; set; } = string.Empty;
    [MaxLength(512)] public string RegistryRepository { get; set; } = string.Empty;
    [MaxLength(128)] public string RegistryTag { get; set; } = string.Empty;
    [MaxLength(1024)] public string? ErrorMessage { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public BootstrapProfile Profile { get; set; } = null!;
    public UserInfo CreatedBy { get; set; } = null!;
    public List<BootstrapProfileDistribution> Distributions { get; set; } = [];
}

public sealed class BootstrapProfileOperationJob
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OperationId { get; set; }
    public BootstrapProfileOperationAction Action { get; set; }
    public Guid ProfilePublicId { get; set; }
    public int? Version { get; set; }
    [MaxLength(128)] public string? Name { get; set; }
    [MaxLength(1024)] public string? Description { get; set; }
    public string? ManifestJson { get; set; }
    [MaxLength(512)] public string? StagedArtifactPath { get; set; }
    [MaxLength(128)] public string? ArtifactDigest { get; set; }
    public long ArtifactSize { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ApiOperation Operation { get; set; } = null!;
}

public sealed class BootstrapProfileDistribution
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public long ProfileVersionId { get; set; }
    public Guid WorkerNodeId { get; set; }
    [MaxLength(128)] public string ArtifactDigest { get; set; } = string.Empty;
    public BootstrapProfileDistributionStatus Status { get; set; } = BootstrapProfileDistributionStatus.Pending;
    [MaxLength(512)] public string? LocalPath { get; set; }
    [MaxLength(1024)] public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public BootstrapProfileVersion ProfileVersion { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
}
