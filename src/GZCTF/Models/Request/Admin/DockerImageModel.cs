using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// Request model for creating/updating a Docker image.
/// </summary>
public class DockerImageCreateModel
{
    [Required]
    [MinLength(1)]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? ImageTag { get; set; }

    public string? Dockerfile { get; set; }

    public OSType OSType { get; set; } = OSType.Linux;

    [MaxLength(256)]
    public string? ExposedPorts { get; set; }
}

public class DockerImageResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageTag { get; set; }
    public OSType OSType { get; set; }
    public ImageStatus Status { get; set; }
    public long FileSize { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? ExposedPorts { get; set; }

    public static DockerImageResponse FromDockerImage(DockerImage image) => new()
    {
        Id = image.Id,
        Name = image.Name,
        ImageTag = image.ImageTag,
        OSType = image.OSType,
        Status = image.Status,
        FileSize = image.FileSize,
        CreatedAt = image.CreatedAt,
        ExposedPorts = image.ExposedPorts
    };
}
