using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GZCTF.Modules.Content.Domain;

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
    Error = 2,
    Deleting = 3
}

public enum VmRuntimeMode : byte
{
    Managed = 0,
    Opaque = 1
}

public enum VmNetworkMode : byte
{
    Dhcp = 0,
    Preconfigured = 1
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
    /// Last import, pull, or distribution error for operator diagnosis
    /// </summary>
    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether this image is classified as containing known malware
    /// </summary>
    public bool ContainsMalware { get; set; }

    /// <summary>
    /// The image has been verified to consume instance-specific credentials through Cloudbase-Init.
    /// Required for Windows player VM deployment.
    /// </summary>
    public bool SupportsInstanceCredentials { get; set; }

    /// <summary>
    /// SHA256 hash of the image file
    /// </summary>
    [MaxLength(64)]
    public string? ImageHash { get; set; }

    /// <summary>
    /// Original archive file name from upload
    /// </summary>
    [MaxLength(256)]
    public string? OriginalArchiveName { get; set; }

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    [JsonIgnore]
    public List<ImageTemplateCapabilityCertification> CapabilityCertifications { get; set; } = [];

    public VmArtifactStatus VmArtifactStatus { get; set; } = VmArtifactStatus.None;

    public VmRuntimeMode VmRuntimeMode { get; set; } = VmRuntimeMode.Opaque;

    public VmNetworkMode VmNetworkMode { get; set; } = VmNetworkMode.Dhcp;

    public long? PreparedArtifactId { get; set; }

    [JsonIgnore]
    public VmPreparedArtifact? PreparedArtifact { get; set; }

    [JsonIgnore]
    public ImageTemplateRemoteAccess? RemoteAccess { get; set; }
}
