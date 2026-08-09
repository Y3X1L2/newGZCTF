using System.Collections.Concurrent;
using GZCTF.Infrastructure.Cache;
using GZCTF.Modules.Runtime.Application;
using StackExchange.Redis;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class RedisRuntimeSignalWakeup(
    IRedisConnectionProvider connections,
    RedisKeyspace keyspace,
    ILogger<RedisRuntimeSignalWakeup> logger) : IRuntimeSignalWakeup, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<long, TaskCompletionSource>> _waiters = new();
    private readonly SemaphoreSlim _subscribeGate = new(1, 1);
    private long _waiterSequence;
    private ISubscriber? _subscriber;

    public async ValueTask NotifyAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        CompleteLocal(operationId);
        try
        {
            var connection = await connections.GetAsync(cancellationToken);
            if (connection is null) return;
            await connection.GetSubscriber().PublishAsync(ChannelName(), operationId.ToString("N"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Runtime signal wake-up publish failed; persisted-signal polling remains active");
        }
    }

    public async ValueTask WaitAsync(
        Guid operationId,
        TimeSpan maximumWait,
        CancellationToken cancellationToken = default)
    {
        await EnsureSubscribedAsync(cancellationToken);
        var id = Interlocked.Increment(ref _waiterSequence);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operationWaiters = _waiters.GetOrAdd(operationId, static _ => new());
        operationWaiters[id] = completion;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(maximumWait);
        try
        {
            await completion.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            operationWaiters.TryRemove(id, out _);
            if (operationWaiters.IsEmpty) _waiters.TryRemove(operationId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscriber is not null) await _subscriber.UnsubscribeAsync(ChannelName());
        _subscribeGate.Dispose();
    }

    private async ValueTask EnsureSubscribedAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null) return;
        await _subscribeGate.WaitAsync(cancellationToken);
        try
        {
            if (_subscriber is not null) return;
            var connection = await connections.GetAsync(cancellationToken);
            if (connection is null) return;
            var subscriber = connection.GetSubscriber();
            await subscriber.SubscribeAsync(ChannelName(), (_, payload) =>
            {
                if (Guid.TryParse(payload.ToString(), out var operationId)) CompleteLocal(operationId);
            });
            _subscriber = subscriber;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Runtime signal wake-up subscription failed; persisted-signal polling remains active");
        }
        finally
        {
            _subscribeGate.Release();
        }
    }

    private void CompleteLocal(Guid operationId)
    {
        if (!_waiters.TryGetValue(operationId, out var waiters)) return;
        foreach (var completion in waiters.Values) completion.TrySetResult();
    }

    private RedisChannel ChannelName() =>
        new(keyspace.Create(RedisKeyPurpose.WakeUp, "runtime-signal").ToString(),
            RedisChannel.PatternMode.Literal);
}
