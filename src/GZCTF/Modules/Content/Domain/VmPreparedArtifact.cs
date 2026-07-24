using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Modules.Content.Domain;

public enum VmArtifactStatus : byte
{
    None = 0,
    Building = 1,
    Ready = 2,
    Failed = 3,
    Invalidated = 4
}

public enum VmPreparedArtifactStatus : byte
{
    Preparing = 0,
    Ready = 1,
    Failed = 2,
    Invalidated = 3
}

public sealed class VmPreparedArtifact
{
    [Key] public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public OSType OSType { get; set; }
    public VmPreparedArtifactStatus Status { get; set; } = VmPreparedArtifactStatus.Preparing;
    [MaxLength(128)] public string ArtifactDigest { get; set; } = string.Empty;
    public long ArtifactSize { get; set; }
    [MaxLength(256)] public string RegistryAddress { get; set; } = string.Empty;
    [MaxLength(512)] public string RegistryRepository { get; set; } = string.Empty;
    [MaxLength(128)] public string RegistryTag { get; set; } = string.Empty;
    [MaxLength(128)] public string? EvidenceDigest { get; set; }
    [MaxLength(1024)] public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PreparedAt { get; set; }
    public ImageTemplate? DerivedImageTemplate { get; set; }
}
