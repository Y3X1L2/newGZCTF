using System.Collections.Concurrent;

namespace GZCTF.Agent.Services;

public sealed class ImageTransferSingleFlight
{
    readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _operations = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string key, Func<CancellationToken, Task<T>> operation,
        CancellationToken waiterToken)
    {
        waiterToken.ThrowIfCancellationRequested();
        var lazy = _operations.GetOrAdd(key, _ => CreateSharedOperation(key, operation));
        return (T)(await lazy.Value.WaitAsync(waiterToken))!;
    }

    Lazy<Task<object?>> CreateSharedOperation<T>(string key, Func<CancellationToken, Task<T>> operation)
    {
        Lazy<Task<object?>>? owner = null;
        owner = new Lazy<Task<object?>>(
            () => ExecuteAndRemoveAsync(key, owner!, operation), LazyThreadSafetyMode.ExecutionAndPublication);
        return owner;
    }

    async Task<object?> ExecuteAndRemoveAsync<T>(string key, Lazy<Task<object?>> owner,
        Func<CancellationToken, Task<T>> operation)
    {
        try
        {
            // Waiters may leave independently. Only the actual writer owns completion and
            // its capacity lease; a future request must execute cache validation again.
            return await operation(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _operations.TryRemove(new KeyValuePair<string, Lazy<Task<object?>>>(key, owner));
        }
    }
}
