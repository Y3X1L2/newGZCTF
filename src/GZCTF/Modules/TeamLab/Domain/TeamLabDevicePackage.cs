using System.ComponentModel.DataAnnotations;

namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabDevicePackageArtifactKind : byte
{
    OciImage = 1,
    VmImage = 2
}

/// <summary>
/// Registry entry for an externally produced, immutable device package
/// (industrial protocol emulator, PLC, honeypot, craft device, ...). The
/// platform never builds package content; it only registers the artifact
/// reference, resource requirements and public author-facing metadata.
/// </summary>
public sealed class TeamLabDevicePackage
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    [MaxLength(96)] public string Name { get; set; } = string.Empty;
    [MaxLength(128)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(64)] public string Version { get; set; } = string.Empty;
    public TeamLabDevicePackageArtifactKind ArtifactKind { get; set; }
    [MaxLength(512)] public string ArtifactReference { get; set; } = string.Empty;
    [MaxLength(128)] public string? Digest { get; set; }
    [MaxLength(2048)] public string? Description { get; set; }
    /// <summary>Canonical JSON array of <see cref="TeamLabResourceKind"/> names this package can back.</summary>
    [MaxLength(256)] public string SupportedAssetKindsJson { get; set; } = "[]";
    public int CpuMillis { get; set; }
    public int MemoryMib { get; set; }
    public int StorageGib { get; set; }
    /// <summary>Canonical JSON array of {name, port, protocol} declarations.</summary>
    [MaxLength(2048)] public string PortsJson { get; set; } = "[]";
    /// <summary>Author-configurable public parameter schema; frozen into the release at publish time.</summary>
    [MaxLength(8192)] public string ParameterSchemaJson { get; set; } = "{}";
    /// <summary>Canonical JSON health declaration: {kind, port?, path?, intervalSeconds?}.</summary>
    [MaxLength(1024)] public string HealthDeclarationJson { get; set; } = "{}";
    /// <summary>Canonical JSON array of desensitized protocol event types the package may report.</summary>
    [MaxLength(2048)] public string ProtocolEventTypesJson { get; set; } = "[]";
    public bool IsEnabled { get; set; } = true;
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
