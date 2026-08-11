using System.Collections.Concurrent;

namespace GZCTF.Agent.Services.TeamLab;

internal sealed class KeyedSemaphoreRegistry<TKey> where TKey : notnull
{
    readonly ConcurrentDictionary<TKey, Entry> entries = new();

    public async ValueTask<IDisposable> AcquireAsync(TKey key, CancellationToken cancellationToken)
    {
        Entry entry;
        while (true)
        {
            entry = entries.GetOrAdd(key, static _ => new Entry());
            if (entry.TryAddReference()) break;
            await Task.Yield();
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Lease(this, key, entry);
        }
        catch
        {
            Return(key, entry, acquired: false);
            throw;
        }
    }

    void Return(TKey key, Entry entry, bool acquired)
    {
        if (acquired) entry.Semaphore.Release();
        if (!entry.ReleaseReference()) return;

        if (entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            entries.TryRemove(key, out _);
        entry.Semaphore.Dispose();
    }

    sealed class Entry
    {
        readonly object sync = new();
        int references;
        bool retired;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddReference()
        {
            lock (sync)
            {
                if (retired) return false;
                references++;
                return true;
            }
        }

        public bool ReleaseReference()
        {
            lock (sync)
            {
                references--;
                if (references != 0) return false;
                retired = true;
                return true;
            }
        }
    }

    sealed class Lease(KeyedSemaphoreRegistry<TKey> owner, TKey key, Entry entry) : IDisposable
    {
        int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.Return(key, entry, acquired: true);
        }
    }
}
