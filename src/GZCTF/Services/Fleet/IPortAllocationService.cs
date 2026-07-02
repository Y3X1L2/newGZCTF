using GZCTF.Models.Internal;

namespace GZCTF.Services.Fleet;

/// <summary>
/// 端口分配服务接口，用于分布式调度下统一分配容器公网端口
/// </summary>
public interface IPortAllocationService
{
    /// <summary>
    /// 分配一个可用的公网端口
    /// </summary>
    /// <param name="containerId">容器 ID，用于标识端口归属</param>
    /// <param name="token"></param>
    /// <returns>分配的端口号，0 表示无可用端口</returns>
    Task<int> AllocatePortAsync(Guid containerId, CancellationToken token = default);

    /// <summary>
    /// 释放已分配的端口
    /// </summary>
    /// <param name="port">端口号</param>
    /// <param name="token"></param>
    Task ReleasePortAsync(int port, CancellationToken token = default);

    /// <summary>
    /// Mark an existing port as occupied. Used to rebuild allocator state from DB after restart.
    /// </summary>
    /// <param name="port">端口号</param>
    /// <param name="owner">端口归属描述</param>
    /// <param name="token"></param>
    Task ReserveExistingPortAsync(int port, string owner, CancellationToken token = default);

    /// <summary>
    /// 是否启用 Redis 端口分配（否则降级为本地端口扫描）
    /// </summary>
    bool IsRedisBacked { get; }

    /// <summary>
    /// Current public port allocation range used for new container entries.
    /// </summary>
    PortAllocationRange CurrentRange { get; }
}

public record PortAllocationRange(int Start, int End, string Mode, bool RequiresRedis);
