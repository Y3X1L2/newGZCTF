using System.Threading.Channels;
using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.Runtime.Application;
using StackExchange.Redis;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class RedisDeploymentQueueWakeup(
    IRedisConnectionProvider connections,
    RedisKeyspace keyspace,
    RedisTelemetry telemetry,
    ILogger<RedisDeploymentQueueWakeup> logger) : IDeploymentQueueWakeup, IAsyncDisposable
{
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = false,
        SingleWriter = false
    });
    private readonly SemaphoreSlim _subscribeGate = new(1, 1);
    private ISubscriber? _subscriber;

    public async ValueTask NotifyAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await connections.GetAsync(cancellationToken);
            if (connection is null)
                return;
            await connection.GetSubscriber().PublishAsync(ChannelName(), ticketId.ToString("N"));
            telemetry.RecordOperation(RedisTelemetryPurpose.WakeUp, RedisTelemetryStatus.Success);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            telemetry.RecordOperation(RedisTelemetryPurpose.WakeUp, RedisTelemetryStatus.Failure);
            logger.LogWarning(exception, "Deployment queue wake-up publish failed; PostgreSQL polling remains active");
        }
    }

    public async ValueTask WaitAsync(TimeSpan maximumWait, CancellationToken cancellationToken = default)
    {
        await EnsureSubscribedAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maximumWait);
        try
        {
            await _signals.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscriber is not null)
            await _subscriber.UnsubscribeAsync(ChannelName());
        _subscribeGate.Dispose();
    }

    private async ValueTask EnsureSubscribedAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
            return;
        await _subscribeGate.WaitAsync(cancellationToken);
        try
        {
            if (_subscriber is not null)
                return;
            var connection = await connections.GetAsync(cancellationToken);
            if (connection is null)
                return;
            var subscriber = connection.GetSubscriber();
            await subscriber.SubscribeAsync(ChannelName(), (_, _) => _signals.Writer.TryWrite(true));
            _subscriber = subscriber;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Deployment queue wake-up subscription failed; using PostgreSQL polling");
        }
        finally
        {
            _subscribeGate.Release();
        }
    }

    private RedisChannel ChannelName() =>
        new(keyspace.Create(RedisKeyPurpose.WakeUp, "deployment-queue").ToString(),
            RedisChannel.PatternMode.Literal);
}
