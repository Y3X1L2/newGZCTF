using System.Collections.Concurrent;

namespace GZCTF.Services.Fleet;

public sealed class NodeExecutionGateOptions
{
    public int MaxConcurrentOperationsPerNode { get; init; } = 2;
}

public class NodeExecutionGate
{
    readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
    readonly int _limit;
    readonly ILogger<NodeExecutionGate> _logger;

    public NodeExecutionGate(NodeExecutionGateOptions options, ILogger<NodeExecutionGate> logger)
    {
        _limit = Math.Max(1, options.MaxConcurrentOperationsPerNode);
        _logger = logger;
    }

    public async Task RunAsync(Guid nodeId, Func<CancellationToken, Task> operation, CancellationToken token)
    {
        var gate = _gates.GetOrAdd(nodeId, _ => new SemaphoreSlim(_limit, _limit));
        await gate.WaitAsync(token);
        try
        {
            await operation(token);
        }
        finally
        {
            gate.Release();
            _logger.LogDebug("Released node execution gate for node {NodeId}.", nodeId);
        }
    }

    public async Task RunExclusiveAsync(
        Guid nodeId,
        Func<CancellationToken, Task> operation,
        CancellationToken token)
    {
        var gate = _gates.GetOrAdd(nodeId, _ => new SemaphoreSlim(_limit, _limit));
        var acquired = 0;
        try
        {
            for (; acquired < _limit; acquired++)
                await gate.WaitAsync(token);
            await operation(token);
        }
        finally
        {
            if (acquired > 0)
                gate.Release(acquired);
            _logger.LogDebug("Released exclusive node execution gate for node {NodeId}.", nodeId);
        }
    }
}
