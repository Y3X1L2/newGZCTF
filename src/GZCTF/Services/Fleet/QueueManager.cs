using System.Collections.Concurrent;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;

namespace GZCTF.Services.Fleet;

public class QueueManager
{
    private readonly ConcurrentQueue<DeploymentTarget> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly INodeRepository _nodeRepo;
    private readonly WeightedScheduler _scheduler;
    private readonly ILogger<QueueManager> _logger;

    public QueueManager(INodeRepository nodeRepo, WeightedScheduler scheduler, ILogger<QueueManager> logger)
    { _nodeRepo = nodeRepo; _scheduler = scheduler; _logger = logger; _ = ProcessQueueAsync(); }

    public Task EnqueueAsync(DeploymentTarget target)
    {
        _queue.Enqueue(target);
        _signal.Release();
        return Task.CompletedTask;
    }

    public int QueueLength => _queue.Count;

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            await _signal.WaitAsync();
            while (_queue.TryDequeue(out var target))
            {
                var nodeId = await _scheduler.SelectOptimalNodeAsync(
                    target.Type == TargetType.Vm ? NodeCapability.Kvm : NodeCapability.Docker,
                    CancellationToken.None);
                if (nodeId is null)
                {
                    _queue.Enqueue(target);
                    await Task.Delay(30000);
                    _signal.Release();
                    break;
                }
                target.TargetNodeId = nodeId.Value;
                _logger.LogInformation("Deployment target {Id} assigned to node {NodeId}", target.Id, nodeId);
            }
        }
    }
}
