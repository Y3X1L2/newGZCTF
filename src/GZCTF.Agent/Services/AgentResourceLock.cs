using System.Collections.Concurrent;

namespace GZCTF.Agent.Services;

public sealed class AgentResourceLock
{
    readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken token)
    {
        var entry = _entries.AddOrUpdate(key, _ => new Entry(), (_, current) =>
        {
            Interlocked.Increment(ref current.References);
            return current;
        });
        await entry.Gate.WaitAsync(token);
        return new Releaser(this, key, entry);
    }

    sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int References = 1;
    }

    sealed class Releaser(AgentResourceLock owner, string key, Entry entry) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            entry.Gate.Release();
            if (Interlocked.Decrement(ref entry.References) == 0)
                owner._entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            return ValueTask.CompletedTask;
        }
    }
}
