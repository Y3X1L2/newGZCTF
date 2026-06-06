using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

/// <summary>
/// AWDP Checker/Exp 执行结果
/// </summary>
public class AwdpCheckerTask
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
    /// Checker 执行状态
    /// </summary>
    public CheckerStatus Status { get; set; }

    /// <summary>
    /// 执行结果消息
    /// </summary>
    [MaxLength(1024)]
    public string? Message { get; set; }

    /// <summary>
    /// 执行时间
    /// </summary>
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;
}
