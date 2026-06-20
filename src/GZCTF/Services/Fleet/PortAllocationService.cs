using System.Net.Sockets;
using System.Net;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GZCTF.Services.Fleet;

/// <summary>
/// 端口分配服务，使用 Redis Lua 脚本原子分配端口。
/// Redis 不可用时降级为本地端口扫描（仅适用于单节点模式）。
/// </summary>
public class PortAllocationService : IPortAllocationService, IDisposable
{
    private readonly ILogger<PortAllocationService> _logger;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IDatabase? _database;
    private readonly int _portStart;
    private readonly int _portEnd;
    private readonly bool _requiresRedis;

    // 原子分配端口的 Lua 脚本：遍历端口段，找到第一个未被占用的端口并设置
    private static readonly LuaScript AllocateScript = LuaScript.Prepare(@"
        for port = @start, @end do
            local key = 'gzctf:port:' .. port
            if redis.call('SETNX', key, @containerId) == 1 then
                redis.call('EXPIRE', key, 7200)
                return port
            end
        end
        return 0
    ");

    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "redis.call('DEL', 'gzctf:port:' .. @port)");

    public bool IsRedisBacked => _database is not null;

    public PortAllocationService(
        IConfiguration config,
        IOptions<ContainerProvider> containerProvider,
        ILogger<PortAllocationService> logger)
    {
        _logger = logger;

        var providerConfig = containerProvider.Value;
        var nginxConfig = providerConfig.NginxProxyConfig;
        var dockerConfig = providerConfig.DockerConfig;
        if (nginxConfig?.Enable == true)
        {
            _portStart = nginxConfig.ListenPortStart;
            _portEnd = nginxConfig.ListenPortEnd;
            _requiresRedis = true;
        }
        else
        {
            _portStart = dockerConfig?.PublicPortStart ?? 30000;
            _portEnd = dockerConfig?.PublicPortEnd ?? 30999;
        }

        var connectionString = config.GetConnectionString("RedisCache");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (_requiresRedis)
                logger.LogError("Nginx proxy mode is enabled, but RedisCache is not configured; public port allocation will fail");
            else
                logger.LogWarning("PortAllocationService is using local fallback because RedisCache is not configured");
            return;
        }

        try
        {
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _database = _redis.GetDatabase();
            logger.LogInformation("PortAllocationService initialized with Redis backing, port range {Start}-{End}",
                _portStart, _portEnd);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "PortAllocationService failed to connect to Redis, falling back to local port scan");
        }
    }

    public async Task<int> AllocatePortAsync(Guid containerId, CancellationToken token = default)
    {
        if (_database is not null)
        {
            try
            {
                var result = (long)await _database.ScriptEvaluateAsync(AllocateScript, new
                {
                    start = _portStart,
                    end = _portEnd,
                    containerId = containerId.ToString()
                });

                if (result > 0)
                {
                    _logger.LogDebug("Allocated port {Port} for container {ContainerId} via Redis",
                        result, containerId);
                    return (int)result;
                }

                _logger.LogWarning("No available port in range {Start}-{End} (Redis allocation exhausted)",
                    _portStart, _portEnd);
                return 0;
            }
            catch (Exception ex)
            {
                if (_requiresRedis)
                {
                    _logger.LogError(ex,
                        "Redis port allocation failed while Nginx proxy mode is enabled; refusing local fallback to avoid duplicate public ports");
                    return 0;
                }

                _logger.LogWarning(ex, "Redis port allocation failed, falling back to local scan");
            }
        }

        if (_requiresRedis)
        {
            _logger.LogError("Nginx proxy mode requires RedisCache for public port allocation");
            return 0;
        }

        // 降级：本地端口扫描
        return await AllocateLocalAsync(token);
    }

    public async Task ReleasePortAsync(int port, CancellationToken token = default)
    {
        if (port <= 0)
            return;

        if (_database is not null)
        {
            try
            {
                await _database.ScriptEvaluateAsync(ReleaseScript, new { port });
                _logger.LogDebug("Released port {Port} via Redis", port);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis port release failed for port {Port}", port);
            }
        }

        // 本地模式无需释放
        await Task.CompletedTask;
    }

    public async Task ReserveExistingPortAsync(int port, string owner, CancellationToken token = default)
    {
        if (port < _portStart || port > _portEnd)
            return;

        if (_database is not null)
        {
            try
            {
                var key = $"gzctf:port:{port}";
                await _database.StringSetAsync(key, owner, TimeSpan.FromHours(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis port reservation refresh failed for port {Port}", port);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 本地端口扫描降级方案（仅适用于单节点，无法跨节点协调）
    /// </summary>
    async Task<int> AllocateLocalAsync(CancellationToken token)
    {
        for (var port = _portStart; port <= _portEnd; port++)
        {
            token.ThrowIfCancellationRequested();
            if (IsTcpPortAvailable(port))
                return port;
        }

        _logger.LogWarning("No available local port in range {Start}-{End}", _portStart, _portEnd);
        return 0;
    }

    static bool IsTcpPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public void Dispose() => _redis?.Dispose();
}
