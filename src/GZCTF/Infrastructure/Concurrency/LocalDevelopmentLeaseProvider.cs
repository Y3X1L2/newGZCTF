using System.Security.Cryptography;

namespace GZCTF.Infrastructure.Concurrency;

public sealed class LocalDevelopmentLeaseProvider : IDistributedLeaseProvider
{
    private static readonly Lock StateGate = new();
    private static readonly Dictionary<string, LocalLockState> Locks = new(StringComparer.Ordinal);

    public async ValueTask<IDistributedLease> AcquireAsync(string resource, TimeSpan? waitTimeout = null,
        TimeSpan? leaseDuration = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        LocalLockState state;
        lock (StateGate)
        {
            if (!Locks.TryGetValue(resource, out state!))
                Locks.Add(resource, state = new LocalLockState());
            state.References++;
        }

        try
        {
            if (!await state.Semaphore.WaitAsync(waitTimeout ?? TimeSpan.FromSeconds(30), cancellationToken))
                throw new TimeoutException($"Timed out acquiring local lease '{resource}'.");
            return new LocalLease(resource, state,
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        }
        catch
        {
            ReleaseReference(resource, state);
            throw;
        }
    }

    private static void ReleaseReference(string resource, LocalLockState state)
    {
        lock (StateGate)
        {
            state.References--;
            if (state.References == 0)
                Locks.Remove(resource);
        }
    }

    private sealed class LocalLockState
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class LocalLease(string resource, LocalLockState state, string ownerToken) : IDistributedLease
    {
        private int _disposed;
        public string Resource => resource;
        public string OwnerToken => ownerToken;
        public CancellationToken LeaseLost => CancellationToken.None;
        public ValueTask<bool> RenewAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                state.Semaphore.Release();
                ReleaseReference(resource, state);
            }
            return ValueTask.CompletedTask;
        }
    }
}
