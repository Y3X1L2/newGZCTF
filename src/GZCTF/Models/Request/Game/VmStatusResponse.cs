namespace GZCTF.Models.Request.Game;

/// <summary>
/// Response model for VM instance status queries.
/// </summary>
public class VmStatusResponse
{
    /// <summary>
    /// VM instance ID
    /// </summary>
    public Guid VmInstanceId { get; set; }

    /// <summary>
    /// Current status: Creating, Running, Stopped, Destroyed, Error
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// VM IP address (null if not yet assigned)
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Guacamole RDP URL (null if not yet ready)
    /// </summary>
    public string? RdpUrl { get; set; }

    /// <summary>
    /// When the VM was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
