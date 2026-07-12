using System.Net;
using System.Net.Sockets;
using GZCTF.Infrastructure.Cache;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GZCTF.Services.Fleet;

public sealed class PortAllocationService(
    IRedisConnectionProvider connections,
    RedisKeyspace keyspace,
    RedisTelemetry telemetry,
    IOptions<ContainerProvider> containerProvider,
    ILogger<PortAllocationService> logger) : IPortAllocationService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromHours(2);
    private static readonly LuaScript AllocateScript = LuaScript.Prepare("""
        for port = @start, @finish do
            local key = @prefix .. ':' .. port
            if redis.call('set', key, @owner, 'NX', 'PX', @ttl) then
                return port
            end
        end
        return 0
        """);
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @owner then return redis.call('del', @key) else return 0 end");
    private static readonly LuaScript ReserveScript = LuaScript.Prepare("""
        local current = redis.call('get', @key)
        if not current then
            redis.call('set', @key, @owner, 'PX', @ttl)
            return 1
        end
        if current == @owner then
            redis.call('pexpire', @key, @ttl)
            return 1
        end
        return 0
        """);

    private readonly int _portStart = ResolveRange(containerProvider.Value).Start;
    private readonly int _portEnd = ResolveRange(containerProvider.Value).End;
    private readonly string _mode = ResolveRange(containerProvider.Value).Mode;
    private readonly bool _requiresRedis = ResolveRange(containerProvider.Value).RequiresRedis;

    public bool IsRedisBacked => connections.IsConfigured;
    public PortAllocationRange CurrentRange => new(_portStart, _portEnd, _mode, _requiresRedis);

    public async Task<PortLease?> AllocatePortAsync(Guid containerId, CancellationToken token = default)
    {
        var leaseId = Guid.CreateVersion7();
        var connection = await GetConnectionAsync(token);
        if (connection is not null)
        {
            try
            {
                var prefix = keyspace.CreateTagged(RedisKeyPurpose.Lease, "port", "public").ToString();
                var result = (long)await connection.GetDatabase().ScriptEvaluateAsync(AllocateScript, new
                {
                    start = _portStart,
                    finish = _portEnd,
                    prefix,
                    owner = Owner(leaseId),
                    ttl = (long)LeaseDuration.TotalMilliseconds
                });
                if (result > 0)
                {
                    telemetry.RecordOperation(RedisTelemetryPurpose.Lease, RedisTelemetryStatus.Success);
                    return new((int)result, leaseId, DateTimeOffset.UtcNow + LeaseDuration);
                }

                logger.LogWarning("Public port lease range {Start}-{End} is exhausted", _portStart, _portEnd);
                return null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                telemetry.RecordOperation(RedisTelemetryPurpose.Lease, RedisTelemetryStatus.Failure);
                if (_requiresRedis || connections.Mode == RedisRuntimeMode.Distributed)
                    throw new InvalidOperationException("Redis public port allocation failed closed.", exception);
                logger.LogWarning(exception, "Redis public port allocation failed; using single-instance scan");
            }
        }

        if (_requiresRedis || connections.Mode == RedisRuntimeMode.Distributed)
            throw new InvalidOperationException("Public port allocation requires a healthy Redis lease backend.");

        var localPort = await AllocateLocalAsync(token);
        return localPort == 0 ? null : new(localPort, leaseId, DateTimeOffset.UtcNow + LeaseDuration);
    }

    public async Task<bool> ReleasePortAsync(int port, Guid leaseId, CancellationToken token = default)
    {
        if (port <= 0 || leaseId == Guid.Empty)
            return false;
        var connection = await GetConnectionAsync(token);
        if (connection is null)
            return !_requiresRedis && connections.Mode != RedisRuntimeMode.Distributed;

        var released = (long)await connection.GetDatabase().ScriptEvaluateAsync(ReleaseScript, new
        {
            key = PortKey(port),
            owner = Owner(leaseId)
        }) == 1;
        telemetry.RecordOperation(RedisTelemetryPurpose.Lease,
            released ? RedisTelemetryStatus.Success : RedisTelemetryStatus.Failure);
        if (!released)
            logger.LogWarning("Public port {Port} was not released because the owner lease did not match", port);
        return released;
    }

    public async Task<bool> ReserveExistingPortAsync(int port, Guid leaseId,
        CancellationToken token = default)
    {
        if (port < _portStart || port > _portEnd || leaseId == Guid.Empty)
            return false;
        var connection = await GetConnectionAsync(token);
        if (connection is null)
            return !_requiresRedis && connections.Mode != RedisRuntimeMode.Distributed;

        var reserved = (long)await connection.GetDatabase().ScriptEvaluateAsync(ReserveScript, new
        {
            key = PortKey(port),
            owner = Owner(leaseId),
            ttl = (long)LeaseDuration.TotalMilliseconds
        }) == 1;
        if (!reserved)
            logger.LogError("Public port {Port} is owned by another lease; reservation failed closed", port);
        return reserved;
    }

    private async ValueTask<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken token)
    {
        try
        {
            return await connections.GetAsync(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_requiresRedis || connections.Mode == RedisRuntimeMode.Distributed)
                throw;
            logger.LogWarning(exception, "Redis lease backend is unavailable in single-instance mode");
            return null;
        }
    }

    private RedisKey PortKey(int port) =>
        keyspace.CreateTagged(RedisKeyPurpose.Lease, "port", "public", port.ToString());

    private static string Owner(Guid leaseId) => leaseId.ToString("N");

    private async Task<int> AllocateLocalAsync(CancellationToken token)
    {
        for (var port = _portStart; port <= _portEnd; port++)
        {
            token.ThrowIfCancellationRequested();
            if (IsTcpPortAvailable(port))
                return port;
            await Task.Yield();
        }
        return 0;
    }

    private static bool IsTcpPortAvailable(int port)
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

    private static PortAllocationRange ResolveRange(ContainerProvider provider)
    {
        if (provider.NginxProxyConfig?.Enable == true)
            return new(provider.NginxProxyConfig.ListenPortStart, provider.NginxProxyConfig.ListenPortEnd,
                "nginx", true);
        var docker = provider.DockerConfig;
        return new(docker?.PublicPortStart ?? 30000, docker?.PublicPortEnd ?? 30999,
            docker?.PublicPortStart is null || docker.PublicPortEnd is null ? "docker-random" : "docker", false);
    }
}
