using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

/// <summary>
/// AWDP 每轮每队每服务的 Flag
/// </summary>
public class AwdpFlag
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 关联轮次 ID
    /// </summary>
    public int RoundId { get; set; }

    /// <summary>
    /// 关联轮次
    /// </summary>
    public AwdpRound Round { get; set; } = null!;

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
    /// Flag 值
    /// </summary>
    [Required]
    [MaxLength(Limits.MaxFlagLength)]
    public string FlagValue { get; set; } = string.Empty;

    /// <summary>
    /// 是否已被其他队伍提交
    /// </summary>
    public bool IsSubmitted { get; set; }

    /// <summary>
    /// 首次被提交的时间
    /// </summary>
    public DateTimeOffset? FirstSubmittedAt { get; set; }
}
