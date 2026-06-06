using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

/// <summary>
/// Persistent record of a created VM instance.
/// Eliminates the need to reconstruct VM names from patterns on destroy/reset.
/// </summary>
[Index(nameof(ChallengeId))]
[Index(nameof(Status))]
public class VmInstance
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public int ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public string VmName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public OSType OSType { get; set; } = OSType.Windows;
    public VmInstanceStatus Status { get; set; } = VmInstanceStatus.Creating;
    public string? SnapshotName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DestroyedAt { get; set; }

    /// <summary>
    /// IP address of the running VM (populated by VmReadyService after boot).
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Guacamole connection identifier for RDP access.
    /// </summary>
    public string? GuacamoleConnectionId { get; set; }

    /// <summary>
    /// RDP username for this VM instance.
    /// </summary>
    public string RdpUsername { get; set; } = "player";

    /// <summary>
    /// RDP password for this VM instance.
    /// </summary>
    public string RdpPassword { get; set; } = "qwer1234!";

    /// <summary>
    /// Full Guacamole URL for the user to access this VM via browser.
    /// </summary>
    public string? RdpUrl { get; set; }

    [Timestamp]
    public uint ConcurrencyToken { get; set; }

    #region Db Relationship
    [ForeignKey(nameof(ChallengeId))]
    public GameChallenge? Challenge { get; set; }

    public Guid? NodeId { get; set; }

    [ForeignKey(nameof(NodeId))]
    public WorkerNode? Node { get; set; }
    #endregion
}

public enum VmInstanceStatus : byte
{
    Creating = 0,
    Running = 1,
    Stopped = 2,
    Destroyed = 3,
    Error = 4
}
