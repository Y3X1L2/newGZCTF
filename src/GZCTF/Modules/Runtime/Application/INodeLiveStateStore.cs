using GZCTF.Modules.Runtime.Contracts;

namespace GZCTF.Modules.Runtime.Application;

public interface INodeLiveStateStore
{
    TimeSpan FreshnessTtl { get; }

    ValueTask<NodeLiveStateWriteResult> WriteAsync(NodeLiveState state,
        CancellationToken cancellationToken = default);

    ValueTask<NodeLiveState?> GetAsync(Guid workerNodeId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyDictionary<Guid, NodeLiveState>> GetManyAsync(
        IReadOnlyCollection<Guid> workerNodeIds,
        CancellationToken cancellationToken = default);
}

internal interface INodeMetricStreamSource
{
    Task<IReadOnlyList<NodeMetricStreamEntry>> ReadBatchAsync(int maximumCount,
        CancellationToken cancellationToken);

    Task AcknowledgeAsync(IReadOnlyCollection<string> entryIds, CancellationToken cancellationToken);
}
