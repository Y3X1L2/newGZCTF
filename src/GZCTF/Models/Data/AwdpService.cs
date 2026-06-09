using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

/// <summary>
/// AWDP 服务/题目定义
/// </summary>
public class AwdpService
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
    /// 服务名称
    /// </summary>
    [Required]
    [MaxLength(Limits.MaxServiceNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 容器镜像名
    /// </summary>
    [Required]
    [MaxLength(Limits.MaxImageNameLength)]
    public string ImageName { get; set; } = string.Empty;

    /// <summary>
    /// 暴露端口
    /// </summary>
    public int ExposePort { get; set; }

    // ===== Checker (功能验证) =====

    /// <summary>
    /// Checker 脚本内容
    /// </summary>
    [MaxLength(Limits.MaxScriptLength)]
    public string? CheckerScript { get; set; }

    /// <summary>
    /// Checker 入口命令
    /// </summary>
    [MaxLength(Limits.MaxEntrypointLength)]
    public string? CheckerEntrypoint { get; set; } = "python3 checker.py";

    // ===== Exp (漏洞验证) =====

    /// <summary>
    /// Exp 脚本内容 (验证漏洞是否被修补)
    /// </summary>
    [MaxLength(Limits.MaxScriptLength)]
    public string? ExpScript { get; set; }

    /// <summary>
    /// Exp 入口命令
    /// </summary>
    [MaxLength(Limits.MaxEntrypointLength)]
    public string? ExpEntrypoint { get; set; } = "python3 exp.py";

    // ===== 分数配置 =====

    /// <summary>
    /// 原始分数
    /// </summary>
    public int OriginalScore { get; set; } = 1000;

    /// <summary>
    /// 攻击得分
    /// </summary>
    public int AttackPoints { get; set; } = 50;

    /// <summary>
    /// SLA 得分 (每轮 Checker 通过)
    /// </summary>
    public int SlaPoints { get; set; } = 20;

    /// <summary>
    /// 修补成功得分
    /// </summary>
    public int PatchPoints { get; set; } = 100;

    /// <summary>
    /// 服务异常扣分 (修补导致 Checker 失败)
    /// </summary>
    public int ServiceAbnormalPenalty { get; set; } = 200;

    /// <summary>
    /// 每轮最大攻击次数
    /// </summary>
    public int MaxAttackPerRound { get; set; } = 3;

    // ===== 轮次配置 =====

    /// <summary>
    /// 攻击阶段时长 (分钟)
    /// </summary>
    public int AttackPhaseMinutes { get; set; } = 15;

    /// <summary>
    /// 修补阶段时长 (分钟)
    /// </summary>
    public int PatchPhaseMinutes { get; set; } = 10;

    /// <summary>
    /// 总轮数
    /// </summary>
    public int TotalRounds { get; set; } = 20;

    // ===== 重置/恢复配置 =====

    /// <summary>
    /// 最大重置次数 (选手自助重置容器)
    /// </summary>
    public int MaxResetCount { get; set; } = 10;

    /// <summary>
    /// 最大一键恢复次数 (修补异常后恢复)
    /// </summary>
    public int MaxRecoveryCount { get; set; } = 5;

    // ===== 导航属性 =====

    /// <summary>
    /// 该服务的所有容器实例
    /// </summary>
    public List<AwdpServiceInstance> Instances { get; set; } = [];

}
