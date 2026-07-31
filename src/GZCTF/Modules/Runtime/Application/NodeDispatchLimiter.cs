using System.Collections.Concurrent;
using GZCTF.Modules.Runtime.Contracts;

namespace GZCTF.Modules.Runtime.Application;

public enum NodeDispatchCategory : byte
{
    DockerCreate = 1,
    VmCreate = 2,
    DockerImageTransfer = 3,
    VmImageTransfer = 4,
    TeamLabNetwork = 5,
    Control = 6,
    Probe = 7,
    Cleanup = 8
}

public static class NodeDispatchLimitPolicy
{
    public static int Resolve(AgentExecutionLimits? limits, NodeDispatchCategory category)
    {
        var requested = category switch
        {
            NodeDispatchCategory.DockerCreate => limits?.DockerCreates,
            NodeDispatchCategory.VmCreate => limits?.VmCreates,
            NodeDispatchCategory.DockerImageTransfer => limits?.DockerImageTransfers,
            NodeDispatchCategory.VmImageTransfer => limits?.VmImageTransfers,
            NodeDispatchCategory.TeamLabNetwork => limits?.TeamLabNetworkOperations,
            NodeDispatchCategory.Control or NodeDispatchCategory.Probe or NodeDispatchCategory.Cleanup =>
                limits?.ControlOperations,
            _ => null
        };
        var safetyCap = category switch
        {
            NodeDispatchCategory.DockerCreate => 16,
            NodeDispatchCategory.VmCreate => 4,
            NodeDispatchCategory.DockerImageTransfer or NodeDispatchCategory.VmImageTransfer => 4,
            NodeDispatchCategory.TeamLabNetwork => 4,
            NodeDispatchCategory.Probe => 16,
            NodeDispatchCategory.Control => 8,
            NodeDispatchCategory.Cleanup => 4,
            _ => 1
        };
        return Math.Clamp(requested.GetValueOrDefault(1), 1, safetyCap);
    }
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

    public async Task<T> RunAsync<T>(Guid nodeId, NodeDispatchCategory category, int limit,
        Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        var normalizedLimit = Math.Max(1, limit);
        var gate = _gates.GetOrAdd((nodeId, category), _ => new DispatchGate(normalizedLimit));
        await gate.Semaphore.WaitAsync(token);
        try
        {
            return await operation(token);
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
