using System.Security.Cryptography;
using GZCTF.Infrastructure.Cache;
using StackExchange.Redis;

namespace GZCTF.Infrastructure.Concurrency;

public sealed class RedisDistributedLeaseProvider(
    IRedisConnectionProvider connections,
    RedisKeyspace keyspace,
    RedisTelemetry telemetry,
    ILogger<RedisDistributedLeaseProvider> logger) : IDistributedLeaseProvider
{
    private static readonly LuaScript RenewScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @owner then return redis.call('pexpire', @key, @ttl) else return 0 end");
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @owner then return redis.call('del', @key) else return 0 end");

    public async ValueTask<IDistributedLease> AcquireAsync(string resource, TimeSpan? waitTimeout = null,
        TimeSpan? leaseDuration = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        var connection = await connections.GetAsync(cancellationToken);
        if (connection is null)
            throw new InvalidOperationException("A distributed lease requires configured Redis.");

        var database = connection.GetDatabase();
        var key = keyspace.CreateOpaque(RedisKeyPurpose.Lock, "resource", resource);
        var owner = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var wait = waitTimeout ?? TimeSpan.FromSeconds(30);
        var duration = leaseDuration ?? TimeSpan.FromSeconds(30);
        if (wait <= TimeSpan.Zero || duration < TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease timing is invalid.");

        var deadline = DateTimeOffset.UtcNow + wait;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await database.StringSetAsync(key, owner, duration, When.NotExists))
                {
                    telemetry.RecordOperation(RedisTelemetryPurpose.Lock, RedisTelemetryStatus.Success);
                    return new RedisLease(resource, key, owner, duration, database, telemetry, logger);
                }
            }
            catch (RedisException exception)
            {
                telemetry.RecordOperation(RedisTelemetryPurpose.Lock, RedisTelemetryStatus.Failure);
                throw new InvalidOperationException($"Redis lease acquisition failed for '{resource}'.", exception);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        } while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException($"Timed out acquiring distributed lease '{resource}'.");
    }

    private sealed class RedisLease : IDistributedLease
    {
        private readonly RedisKey _key;
        private readonly TimeSpan _duration;
        private readonly IDatabase _database;
        private readonly RedisTelemetry _telemetry;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _leaseLost = new();
        private readonly CancellationTokenSource _renewalStop = new();
        private readonly Task _renewal;
        private int _disposed;

        public RedisLease(string resource, RedisKey key, string ownerToken, TimeSpan duration, IDatabase database,
            RedisTelemetry telemetry, ILogger logger)
        {
            Resource = resource;
            _key = key;
            OwnerToken = ownerToken;
            _duration = duration;
            _database = database;
            _telemetry = telemetry;
            _logger = logger;
            _renewal = RenewUntilDisposedAsync();
        }

        public string Resource { get; }
        public string OwnerToken { get; }
        public CancellationToken LeaseLost => _leaseLost.Token;

        public async ValueTask<bool> RenewAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;
            try
            {
                var renewed = (long)await _database.ScriptEvaluateAsync(RenewScript,
                    new { key = _key, owner = OwnerToken, ttl = (long)_duration.TotalMilliseconds }) == 1;
                if (!renewed)
                    MarkLost("owner-mismatch");
                return renewed;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                MarkLost("renew-failed");
                _logger.LogWarning(exception, "Distributed lease renewal failed for {Resource}", Resource);
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _renewalStop.Cancel();
            try
            {
                await _renewal;
            }
            catch (OperationCanceledException) when (_renewalStop.IsCancellationRequested)
            {
            }

            try
            {
                await _database.ScriptEvaluateAsync(ReleaseScript,
                    new { key = _key, owner = OwnerToken });
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Distributed lease release failed for {Resource}", Resource);
            }
            finally
            {
                _renewalStop.Dispose();
                _leaseLost.Dispose();
            }
        }

        private async Task RenewUntilDisposedAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_duration.TotalMilliseconds / 3));
            while (await timer.WaitForNextTickAsync(_renewalStop.Token))
                if (!await RenewAsync(_renewalStop.Token))
                    return;
        }

        private void MarkLost(string reason)
        {
            if (!_leaseLost.IsCancellationRequested)
                _leaseLost.Cancel();
            _telemetry.RecordOperation(RedisTelemetryPurpose.Lock, RedisTelemetryStatus.Failure);
            _logger.LogWarning("Distributed lease lost for {Resource}: {Reason}", Resource, reason);
        }
    }
}
