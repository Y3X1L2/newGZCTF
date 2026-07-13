using System.Collections.Concurrent;

namespace GZCTF.Agent.Services;

public sealed class ImageTransferSingleFlight
{
    readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _operations = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string key, Func<CancellationToken, Task<T>> operation,
        CancellationToken waiterToken)
    {
        var lazy = _operations.GetOrAdd(key, _ => new Lazy<Task<object?>>(
            async () => await operation(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return (T)(await lazy.Value.WaitAsync(waiterToken))!;
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
                _operations.TryRemove(new KeyValuePair<string, Lazy<Task<object?>>>(key, lazy));
        }
    }
}
