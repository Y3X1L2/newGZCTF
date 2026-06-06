using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

/// <summary>
/// AWDP 修补包提交记录
/// </summary>
public class AwdpPatchSubmission
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
    /// 修补包文件哈希 (SHA256)
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string PatchFileHash { get; set; } = string.Empty;

    /// <summary>
    /// 提交时间
    /// </summary>
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Checker 验证结果
    /// </summary>
    public CheckerStatus CheckerResult { get; set; }

    /// <summary>
    /// Exp 验证结果
    /// </summary>
    public AwdpPatchStatus ExpResult { get; set; } = AwdpPatchStatus.Pending;

    /// <summary>
    /// 最终状态
    /// </summary>
    public AwdpPatchStatus FinalStatus { get; set; } = AwdpPatchStatus.Pending;

    /// <summary>
    /// 验证结果消息
    /// </summary>
    [MaxLength(1024)]
    public string? Message { get; set; }
}
