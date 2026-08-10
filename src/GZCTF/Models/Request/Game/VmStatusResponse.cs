using GZCTF.Services.Fleet;

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
    /// Current deployment stage: image-pending, image-pulling, vm-creating, vm-booting, ready, error
    /// </summary>
    public string? Stage { get; set; }

    /// <summary>
    /// Human-readable deployment stage label
    /// </summary>
    public string? StageMessage { get; set; }

    /// <summary>
    /// Deployment queue status when the VM is waiting or being created
    /// </summary>
    public DeploymentQueueStatusModel? Queue { get; set; }

    /// <summary>
    /// VM IP address (null if not yet assigned)
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Worker address for native RDP access (null until RDP is ready)
    /// </summary>
    public string? RdpHost { get; set; }

    /// <summary>
    /// Worker proxy port for native RDP access (null until RDP is ready)
    /// </summary>
    public int? RdpPort { get; set; }

    /// <summary>
    /// Fixed username configured on the image template
    /// </summary>
    public string? RdpUsername { get; set; }

    /// <summary>
    /// Fixed password configured on the image template
    /// </summary>
    public string? RdpPassword { get; set; }

    /// <summary>
    /// Guacamole RDP URL (null if not yet ready)
    /// </summary>
    public string? RdpUrl { get; set; }

    /// <summary>
    /// When the VM was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
