using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Game;

/// <summary>
/// AWDP 服务创建模型
/// </summary>
public class AwdpServiceCreateModel
{
    /// <summary>
    /// 服务名称
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 容器镜像名
    /// </summary>
    [Required]
    public string ImageName { get; set; } = string.Empty;

    /// <summary>
    /// 暴露端口
    /// </summary>
    public int ExposePort { get; set; } = 80;

    /// <summary>
    /// Checker 脚本内容
    /// </summary>
    public string? CheckerScript { get; set; }

    /// <summary>
    /// Checker 入口命令
    /// </summary>
    public string? CheckerEntrypoint { get; set; } = "python checker.py";

    /// <summary>
    /// Exp 脚本内容
    /// </summary>
    public string? ExpScript { get; set; }

    /// <summary>
    /// Exp 入口命令
    /// </summary>
    public string? ExpEntrypoint { get; set; } = "python exp.py";

    /// <summary>
    /// 原始分数
    /// </summary>
    public int OriginalScore { get; set; } = 1000;

    /// <summary>
    /// 攻击得分
    /// </summary>
    public int AttackPoints { get; set; } = 50;

    /// <summary>
    /// SLA 得分
    /// </summary>
    public int SlaPoints { get; set; } = 20;

    /// <summary>
    /// 修补成功得分
    /// </summary>
    public int PatchPoints { get; set; } = 100;

    /// <summary>
    /// 服务异常扣分
    /// </summary>
    public int ServiceAbnormalPenalty { get; set; } = 200;

    /// <summary>
    /// 每轮最大攻击次数
    /// </summary>
    public int MaxAttackPerRound { get; set; } = 3;

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

    /// <summary>
    /// 最大重置次数
    /// </summary>
    public int MaxResetCount { get; set; } = 10;

    /// <summary>
    /// 最大一键恢复次数
    /// </summary>
    public int MaxRecoveryCount { get; set; } = 5;
}

/// <summary>
/// AWDP 服务更新模型
/// </summary>
public class AwdpServiceUpdateModel : AwdpServiceCreateModel
{
}

/// <summary>
/// AWDP 服务视图模型
/// </summary>
public class AwdpServiceViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public int ExposePort { get; set; }
    public string? CheckerScript { get; set; }
    public string? CheckerEntrypoint { get; set; }
    public string? ExpScript { get; set; }
    public string? ExpEntrypoint { get; set; }
    public int OriginalScore { get; set; }
    public int AttackPoints { get; set; }
    public int SlaPoints { get; set; }
    public int PatchPoints { get; set; }
    public int ServiceAbnormalPenalty { get; set; }
    public int MaxAttackPerRound { get; set; }
    public int AttackPhaseMinutes { get; set; }
    public int PatchPhaseMinutes { get; set; }
    public int TotalRounds { get; set; }
    public int MaxResetCount { get; set; }
    public int MaxRecoveryCount { get; set; }
}

/// <summary>
/// AWDP Flag 提交模型
/// </summary>
public class AwdpSubmitModel
{
    /// <summary>
    /// Flag 值
    /// </summary>
    [Required]
    public string Flag { get; set; } = string.Empty;
}

/// <summary>
/// AWDP Flag submission result model
/// </summary>
public class AwdpSubmitResultModel
{
    public bool Accepted { get; set; }
    public int Points { get; set; }
    public int RoundNumber { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// AWDP 修补包上传模型
/// </summary>
public class AwdpPatchSubmitModel
{
    /// <summary>
    /// 服务 ID
    /// </summary>
    [Required]
    public int ServiceId { get; set; }

    /// <summary>
    /// 修补包文件，必须是 tar.gz/tgz 归档并包含 update.sh
    /// </summary>
    [Required]
    public IFormFile File { get; set; } = null!;
}

/// <summary>
/// AWDP 容器操作结果
/// </summary>
public class AwdpInstanceActionModel
{
    public int InstanceId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// AWDP 比赛状态模型
/// </summary>
public class AwdpGameStatusModel
{
    public int GameId { get; set; }
    public int CurrentRound { get; set; }
    public DateTimeOffset RoundStartTime { get; set; }
    public int AttackPhaseMinutes { get; set; }
    public int PatchPhaseMinutes { get; set; }
    public AwdpRoundStatus Status { get; set; }
}

/// <summary>
/// AWDP 队伍服务状态
/// </summary>
public class AwdpTeamServiceStatus
{
    public int InstanceId { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int? Port { get; set; }
    public CheckerStatus? LastCheckerStatus { get; set; }
    public bool IsRunning { get; set; }
    public int RemainingResetCount { get; set; }
    public int RemainingRecoveryCount { get; set; }
}

/// <summary>
/// AWDP 排行榜条目
/// </summary>
public class AwdpScoreboardItem
{
    public int Rank { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int CtfScore { get; set; }
    public int AwdpScore { get; set; }
    public int TotalScore => CtfScore + AwdpScore;
    public int AttackScore { get; set; }
    public int SlaScore { get; set; }
    public int PatchScore { get; set; }
    public int PenaltyScore { get; set; }
}

/// <summary>
/// AWDP 攻击日志条目
/// </summary>
public class AwdpAttackLogItem
{
    public DateTimeOffset Time { get; set; }
    public string AttackerTeam { get; set; } = string.Empty;
    public string VictimTeam { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public int Points { get; set; }
}

/// <summary>
/// AWDP 修补包状态条目
/// </summary>
public class AwdpPatchStatusItem
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public AwdpChallengeStatus AttackStatus { get; set; }
    public AwdpChallengeStatus DefenseStatus { get; set; }
    public AwdpPatchStatus? LastPatchResult { get; set; }
    public DateTimeOffset? LastPatchTime { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// AWDP 修补包提交视图
/// </summary>
public class AwdpPatchSubmissionViewModel
{
    public int Id { get; set; }
    public int RoundId { get; set; }
    public int RoundNumber { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string PatchFileHash { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; set; }
    public CheckerStatus CheckerResult { get; set; }
    public AwdpPatchStatus ExpResult { get; set; }
    public AwdpPatchStatus FinalStatus { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// AWDP 服务状态模型 (SignalR 推送用)
/// </summary>
public class AwdpServiceStatusModel
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public List<AwdpTeamServiceStatus> TeamStatuses { get; set; } = [];
}

/// <summary>
/// AWDP 修补结果模型 (SignalR 推送用)
/// </summary>
public class AwdpPatchResultModel
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public AwdpPatchStatus Status { get; set; }
    public string? Message { get; set; }
}
