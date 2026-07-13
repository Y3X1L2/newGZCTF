using System.Collections.Concurrent;

namespace GZCTF.Modules.Runtime.Application;

public enum NodeDispatchCategory : byte
{
    DockerCreate = 1,
    VmCreate = 2,
    DockerImageTransfer = 3,
    VmImageTransfer = 4,
    TeamLabNetwork = 5,
    Control = 6
}

public sealed class NodeDispatchLimiter
{
    readonly ConcurrentDictionary<(Guid NodeId, NodeDispatchCategory Category), DispatchGate> _gates = new();

    public async Task RunAsync(Guid nodeId, NodeDispatchCategory category, int limit,
        Func<CancellationToken, Task> operation, CancellationToken token)
    {
        var normalizedLimit = Math.Max(1, limit);
        var gate = _gates.GetOrAdd((nodeId, category), _ => new DispatchGate(normalizedLimit));
        await gate.Semaphore.WaitAsync(token);
        try
        {
            await operation(token);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    public async Task WaitForIdleAsync(Guid nodeId, NodeDispatchCategory category, CancellationToken token)
    {
        if (!_gates.TryGetValue((nodeId, category), out var gate))
            return;
        var acquired = 0;
        try
        {
            for (; acquired < gate.Capacity; acquired++)
                await gate.Semaphore.WaitAsync(token);
        }
        finally
        {
            if (acquired > 0)
                gate.Semaphore.Release(acquired);
        }
    }

    sealed class DispatchGate(int capacity)
    {
        public int Capacity { get; } = capacity;
        public SemaphoreSlim Semaphore { get; } = new(capacity, capacity);
    }
}
