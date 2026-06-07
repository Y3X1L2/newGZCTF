using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

/// <summary>
/// AWDP 一键恢复记录
/// </summary>
public class AwdpRecoveryRecord
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 关联服务 ID
    /// </summary>
    public int ServiceId { get; set; }

    /// <summary>
    /// 关联服务
    /// </summary>
    public AwdpService Service { get; set; } = null!;

    /// <summary>
    /// 关联队伍 ID
    /// </summary>
    public int TeamId { get; set; }

    /// <summary>
    /// 关联队伍
    /// </summary>
    public Team Team { get; set; } = null!;

    /// <summary>
    /// 恢复时间
    /// </summary>
    public DateTimeOffset RecoveryAt { get; set; } = DateTimeOffset.UtcNow;
}
