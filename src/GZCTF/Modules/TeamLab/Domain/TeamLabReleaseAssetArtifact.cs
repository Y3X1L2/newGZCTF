using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabReleaseArtifactStatus : byte
{
    Baking = 0,
    Ready = 1,
    Failed = 2
}

public sealed class TeamLabReleaseAssetArtifact
{
    [Key] public long Id { get; set; }
    public Guid ReleaseId { get; set; }
    [MaxLength(64)] public string AssetKey { get; set; } = string.Empty;
    public int SourceImageTemplateId { get; set; }
    public int? ScenarioImageTemplateId { get; set; }
    public int? BakeRuntimeId { get; set; }
    public Guid CommitOperationId { get; set; }
    public TeamLabReleaseArtifactStatus Status { get; set; } = TeamLabReleaseArtifactStatus.Baking;
    [MaxLength(64)] public string BuildIdentity { get; set; } = string.Empty;
    [MaxLength(128)] public string ArtifactDigest { get; set; } = string.Empty;
    [MaxLength(128)] public string EvidenceDigest { get; set; } = string.Empty;
    public long ArtifactSize { get; set; }
    [MaxLength(256)] public string RegistryAddress { get; set; } = string.Empty;
    [MaxLength(512)] public string RegistryRepository { get; set; } = string.Empty;
    [MaxLength(128)] public string RegistryTag { get; set; } = string.Empty;
    [MaxLength(1024)] public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadyAt { get; set; }
    public TeamLabTopologyRelease Release { get; set; } = null!;
    public ImageTemplate SourceImageTemplate { get; set; } = null!;
    public ImageTemplate? ScenarioImageTemplate { get; set; }
    public TeamLabRuntime? BakeRuntime { get; set; }
}
