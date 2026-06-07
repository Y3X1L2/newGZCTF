using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

/// <summary>
/// AWDP 容器重置记录
/// </summary>
public class AwdpResetRecord
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
    /// 重置时间
    /// </summary>
    public DateTimeOffset ResetAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 重置类型 (选手自助 / 管理员操作)
    /// </summary>
    public AwdpResetType ResetType { get; set; } = AwdpResetType.Player;
}
