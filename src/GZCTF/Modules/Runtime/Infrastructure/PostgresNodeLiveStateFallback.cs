using System.Collections.Concurrent;
using System.Threading.Channels;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class PostgresNodeLiveStateFallback
{
    internal const int BufferCapacity = 10_000;

    private readonly Channel<NodeLiveState> _buffer = Channel.CreateBounded<NodeLiveState>(
        new BoundedChannelOptions(BufferCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly ConcurrentDictionary<Guid, NodeLiveState> _latest = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public PostgresNodeLiveStateFallback(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public NodeLiveStateWriteResult Buffer(NodeLiveState state)
    {
        while (true)
        {
            if (_latest.TryGetValue(state.WorkerNodeId, out var current))
            {
                if (state.Sequence <= current.Sequence)
                    return NodeLiveStateWriteResult.Rejected;
                if (!_latest.TryUpdate(state.WorkerNodeId, state, current))
                    continue;
            }
            else if (_latest.Count >= BufferCapacity)
            {
                _buffer.Writer.TryWrite(state);
                return NodeLiveStateWriteResult.Buffered;
            }
            else if (!_latest.TryAdd(state.WorkerNodeId, state))
            {
                continue;
            }

            break;
        }

        _buffer.Writer.TryWrite(state);
        return NodeLiveStateWriteResult.Buffered;
    }

    public void Requeue(IEnumerable<NodeLiveState> states)
    {
        foreach (var state in states)
            _buffer.Writer.TryWrite(state);
    }

    public void MarkPersisted(IEnumerable<NodeLiveState> states)
    {
        foreach (var state in states)
        {
            if (_latest.TryGetValue(state.WorkerNodeId, out var current) && current.Sequence <= state.Sequence)
                ((ICollection<KeyValuePair<Guid, NodeLiveState>>)_latest).Remove(
                    new(state.WorkerNodeId, current));
        }
    }

    public IReadOnlyList<NodeLiveState> Drain(int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        var items = new List<NodeLiveState>(maximumCount);
        while (items.Count < maximumCount && _buffer.Reader.TryRead(out var item))
            items.Add(item);
        return items;
    }

    public async ValueTask<NodeLiveState?> GetAsync(Guid workerNodeId, CancellationToken cancellationToken)
    {
        if (_latest.TryGetValue(workerNodeId, out var current))
            return current;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var checkpoint = await context.WorkerNodes.AsNoTracking()
            .Where(node => node.Id == workerNodeId && node.LastHeartbeat != null)
            .Select(node => new NodeLiveState(
                node.Id,
                node.LiveMetricSequence,
                node.LiveMetricObservedAt ?? node.LastHeartbeat!.Value,
                node.LiveMetricReceivedAt ?? node.LastHeartbeat!.Value,
                node.CpuLoad,
                node.MemoryLoad,
                node.CurrentContainers,
                node.CurrentVms,
                node.UsedPorts))
            .FirstOrDefaultAsync(cancellationToken);

        return checkpoint;
    }

    public async ValueTask<IReadOnlyDictionary<Guid, NodeLiveState>> GetManyAsync(
        IReadOnlyCollection<Guid> workerNodeIds,
        CancellationToken cancellationToken)
    {
        if (workerNodeIds.Count == 0)
            return new Dictionary<Guid, NodeLiveState>();

        var requested = workerNodeIds.Distinct().ToArray();
        var result = new Dictionary<Guid, NodeLiveState>(requested.Length);
        var missing = new List<Guid>(requested.Length);
        foreach (var workerNodeId in requested)
        {
            if (_latest.TryGetValue(workerNodeId, out var state))
                result[workerNodeId] = state;
            else
                missing.Add(workerNodeId);
        }

        if (missing.Count == 0)
            return result;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var checkpoints = await context.WorkerNodes.AsNoTracking()
            .Where(node => missing.Contains(node.Id) && node.LastHeartbeat != null)
            .Select(node => new NodeLiveState(
                node.Id,
                node.LiveMetricSequence,
                node.LiveMetricObservedAt ?? node.LastHeartbeat!.Value,
                node.LiveMetricReceivedAt ?? node.LastHeartbeat!.Value,
                node.CpuLoad,
                node.MemoryLoad,
                node.CurrentContainers,
                node.CurrentVms,
                node.UsedPorts))
            .ToArrayAsync(cancellationToken);

        foreach (var state in checkpoints)
            result[state.WorkerNodeId] = state;

        return result;
    }
}
