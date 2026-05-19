using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

/// <summary>
/// Docker container image managed by the platform.
/// Can be built from Dockerfile or pulled from registry.
/// </summary>
[Index(nameof(Status))]
public class DockerImage
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Image name (e.g. "nginx-ctf", "ubuntu-web")
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full image tag including registry (e.g. "registry.example.com/nginx-ctf:latest")
    /// </summary>
    [MaxLength(512)]
    public string? ImageTag { get; set; }

    /// <summary>
    /// Dockerfile content for building the image
    /// </summary>
    public string? Dockerfile { get; set; }

    /// <summary>
    /// Target OS type (Linux container by default)
    /// </summary>
    [Required]
    public OSType OSType { get; set; } = OSType.Linux;

    /// <summary>
    /// Image status
    /// </summary>
    [Required]
    public ImageStatus Status { get; set; } = ImageStatus.Ready;

    /// <summary>
    /// File size of the built image in bytes (0 if not yet built)
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Exposed ports
    /// </summary>
    [MaxLength(256)]
    public string? ExposedPorts { get; set; }

    /// <summary>
    /// Environment variables (JSON)
    /// </summary>
    [MaxLength(2048)]
    public string? EnvironmentVars { get; set; }
}
