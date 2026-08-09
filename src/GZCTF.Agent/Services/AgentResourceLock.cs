using System.Collections.Concurrent;

namespace GZCTF.Agent.Services;

public sealed class AgentResourceLock
{
    readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken token)
    {
        while (true)
        {
            var entry = _entries.GetOrAdd(key, static _ => new Entry());
            if (!entry.TryAddReference())
                continue;

            try
            {
                await entry.Gate.WaitAsync(token);
                return new Releaser(this, key, entry);
            }
            catch
            {
                ReleaseReference(key, entry);
                throw;
            }
        }
    }

    sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        int _references;

        public bool TryAddReference()
        {
            while (true)
            {
                var current = Volatile.Read(ref _references);
                if (current < 0)
                    return false;
                if (Interlocked.CompareExchange(ref _references, current + 1, current) == current)
                    return true;
            }
        }

        public bool ReleaseAndTryRetire()
        {
            if (Interlocked.Decrement(ref _references) != 0)
                return false;
            return Interlocked.CompareExchange(ref _references, -1, 0) == 0;
        }
    }

    void ReleaseReference(string key, Entry entry)
    {
        if (entry.ReleaseAndTryRetire())
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
    }

    sealed class Releaser(AgentResourceLock owner, string key, Entry entry) : IAsyncDisposable
    {
        int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;
            entry.Gate.Release();
            owner.ReleaseReference(key, entry);
            return ValueTask.CompletedTask;
        }
    }
}
