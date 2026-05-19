using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public enum OSType : byte
{
    Linux = 0,
    Windows = 1
}

public enum ImageType : byte
{
    Docker = 0,
    Qcow2 = 1,
    Ova = 2,
    Vmdk = 3
}

public enum ImageStatus : byte
{
    Ready = 0,
    Importing = 1,
    Error = 2
}

public class ImageTemplate
{
    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Image template name
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Operating system type
    /// </summary>
    [Required]
    public OSType OSType { get; set; } = OSType.Linux;

    /// <summary>
    /// Image format type
    /// </summary>
    [Required]
    public ImageType ImageType { get; set; } = ImageType.Docker;

    /// <summary>
    /// Registry URL for pulling the image
    /// </summary>
    [MaxLength(512)]
    public string? RegistryUrl { get; set; }

    /// <summary>
    /// Registry authentication token or credentials
    /// </summary>
    [MaxLength(512)]
    public string? RegistryAuth { get; set; }

    /// <summary>
    /// Local file path for imported images
    /// </summary>
    [MaxLength(512)]
    public string? LocalFilePath { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Upload timestamp
    /// </summary>
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Current status of the image
    /// </summary>
    [Required]
    public ImageStatus Status { get; set; } = ImageStatus.Ready;

    /// <summary>
    /// Optional description of the image template
    /// </summary>
    [MaxLength(1024)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this image contains known malware (for IR challenges)
    /// </summary>
    public bool ContainsMalware { get; set; }

    /// <summary>
    /// SHA256 hash of the image file
    /// </summary>
    [MaxLength(64)]
    public string? ImageHash { get; set; }
}
