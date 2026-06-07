using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

/// <summary>
/// AWDP 每队每服务的容器实例
/// </summary>
public class AwdpServiceInstance
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
    /// 关联容器 ID
    /// </summary>
    public Guid? ContainerId { get; set; }

    /// <summary>
    /// 关联容器
    /// </summary>
    public Container? Container { get; set; }

    /// <summary>
    /// Docker 隔离网络名称
    /// </summary>
    [Required]
    [MaxLength(Limits.MaxNetworkNameLength)]
    public string NetworkName { get; set; } = string.Empty;

    /// <summary>
    /// 容器是否运行中
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
