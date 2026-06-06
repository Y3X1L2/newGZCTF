using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

/// <summary>
/// AWDP 轮次记录
/// </summary>
public class AwdpRound
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 关联比赛 ID
    /// </summary>
    public int GameId { get; set; }

    /// <summary>
    /// 关联比赛
    /// </summary>
    public Game Game { get; set; } = null!;

    /// <summary>
    /// 轮次编号 (从 1 开始)
    /// </summary>
    public int RoundNumber { get; set; }

    /// <summary>
    /// 轮次开始时间
    /// </summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// 轮次结束时间
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// 当前阶段状态
    /// </summary>
    public AwdpRoundStatus Status { get; set; } = AwdpRoundStatus.AttackPhase;

    /// <summary>
    /// 攻击阶段开始时间
    /// </summary>
    public DateTimeOffset AttackPhaseStart { get; set; }

    /// <summary>
    /// 修补阶段开始时间
    /// </summary>
    public DateTimeOffset? PatchPhaseStart { get; set; }

    // ===== 导航属性 =====

    /// <summary>
    /// 该轮次的所有 Flag
    /// </summary>
    public List<AwdpFlag> Flags { get; set; } = [];

    /// <summary>
    /// 该轮次的所有 Checker 任务
    /// </summary>
    public List<AwdpCheckerTask> CheckerTasks { get; set; } = [];

    /// <summary>
    /// 该轮次的所有修补包提交
    /// </summary>
    public List<AwdpPatchSubmission> PatchSubmissions { get; set; } = [];
}
