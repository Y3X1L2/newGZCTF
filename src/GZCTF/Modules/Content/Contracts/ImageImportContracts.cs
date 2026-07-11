using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Modules.Content.Contracts;

public sealed class DockerImageReferenceImportModel
{
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string RegistryUrl { get; set; } = string.Empty;

    public OSType OSType { get; set; } = OSType.Linux;

    [MaxLength(128)]
    public string? ExpectedDigest { get; set; }
}

public sealed record DockerImageReferenceImportCommand(
    string Name,
    string RegistryUrl,
    OSType OSType,
    string? ExpectedDigest);

public sealed record DockerImageArchiveImportCommand(
    string Name,
    string? SourceImage,
    OSType OSType,
    string? ExpectedDigest);

public sealed record ImageImportArtifact(
    string RegistryUrl,
    string? ImageHash,
    long ContentLength,
    string Description);
