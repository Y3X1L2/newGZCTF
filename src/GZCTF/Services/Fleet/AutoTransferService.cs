using GZCTF.Models.Data;

namespace GZCTF.Services.Fleet;

public class AutoTransferService
{
    private readonly FleetManager _fleet;
    private readonly ILogger<AutoTransferService> _logger;

    public AutoTransferService(FleetManager fleet, ILogger<AutoTransferService> logger)
    { _fleet = fleet; _logger = logger; }

    public async Task<bool> CheckAndTransferAsync(Guid currentNodeId, DeploymentTarget target, CancellationToken token)
    {
        // If queue is growing, try to transfer to another node
        if (_fleet.QueueLength > 5)
        {
            var newNodeId = await _fleet.TryScheduleAsync(target, token);
            if (newNodeId is not null && newNodeId != currentNodeId)
            {
                _logger.LogWarning("Auto-transferring target {TargetId} from node {Old} to {New}",
                    target.Id, currentNodeId, newNodeId);
                return true;
            }
        }
        return false;
    }
}
