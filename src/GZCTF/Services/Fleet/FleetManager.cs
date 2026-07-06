using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

/// <summary>
/// Main entry point for fleet operations — delegates to WeightedScheduler + QueueManager.
/// </summary>
public class FleetManager
{
    private readonly QueueManager _queue;
    private readonly INodeRepository _nodeRepo;
    private readonly AppDbContext _context;
    private readonly FleetCapacityReservationService _capacity;
    private readonly DeploymentQueueService _queueService;
    private readonly ILogger<FleetManager> _logger;

    public FleetManager(
        QueueManager queue,
        INodeRepository nodeRepo,
        AppDbContext context,
        FleetCapacityReservationService capacity,
        DeploymentQueueService queueService,
        ILogger<FleetManager> logger)
    {
        _queue = queue;
        _nodeRepo = nodeRepo;
        _context = context;
        _capacity = capacity;
        _queueService = queueService;
        _logger = logger;
    }

    public async Task<Guid?> TryScheduleAsync(DeploymentTarget target, CancellationToken token,
        bool queueWhenNoNode = true)
    {
        var result = await TryScheduleWithTargetAsync(target, token, queueWhenNoNode);
        return result.NodeId;
    }

    public async Task<FleetScheduleResult> TryScheduleWithTargetAsync(DeploymentTarget target, CancellationToken token,
        bool queueWhenNoNode = true)
    {
        var capability = GetRequiredCapability(target.Type);
        var reservation = await _capacity.TryReserveAsync(new FleetCapacityRequest(
            capability,
            DockerSlots: capability.HasFlag(NodeCapability.Docker) ? 1 : 0,
            VmSlots: capability.HasFlag(NodeCapability.Kvm) ? 1 : 0), token);
        var nodeId = reservation.NodeId;

        if (nodeId is null)
        {
            if (!queueWhenNoNode)
            {
                _logger.LogInformation("Deployment {Id} ({Type}) was not queued - no node available",
                    target.Id, target.Type);
                return FleetScheduleResult.NotScheduled("No schedulable node available");
            }

            target.Status = TargetStatus.Pending;
            target.TargetNodeId = null;
            target.ErrorMessage = "Waiting for a schedulable node";
            _context.DeploymentTargets.Add(target);
            await _context.SaveChangesAsync(token);
            _logger.SystemLogDeploymentTarget("queued", target);
            _logger.LogInformation("Deployment {Id} ({Type}) queued - no node available",
                target.Id, target.Type);
            var queueStatus = await TryCreateQueueTicketAsync(target, token);
            return FleetScheduleResult.Queued(target, reservation.Message, queueStatus);
        }

        var node = reservation.Node ?? await _nodeRepo.GetNodeByIdAsync(nodeId.Value, token);
        if (node is null)
        {
            _logger.LogWarning("Selected node {NodeId} disappeared before deployment {Id} could be assigned",
                nodeId.Value, target.Id);
            return FleetScheduleResult.NotScheduled("Selected node is no longer available");
        }

        target.TargetNodeId = nodeId.Value;
        target.Status = TargetStatus.Assigned;
        target.ErrorMessage = null;
        _context.DeploymentTargets.Add(target);
        try
        {
            await _context.SaveChangesAsync(token);
        }
        catch
        {
            await ReleaseReservationAfterAssignmentFailureAsync(reservation, token);
            throw;
        }
        _logger.SystemLogDeploymentTarget("assigned", target, node);

        return FleetScheduleResult.Scheduled(nodeId.Value, node, target);
    }

    public async Task<List<WorkerNode>> GetAllNodesAsync(CancellationToken token) =>
        await _nodeRepo.GetAllNodesAsync(token);

    internal static NodeCapability GetRequiredCapability(TargetType type) =>
        type == TargetType.Vm ? NodeCapability.Kvm : NodeCapability.Docker;

    internal static void ReserveCapacity(WorkerNode node, NodeCapability capability)
    {
        if ((capability & NodeCapability.Docker) == NodeCapability.Docker)
            node.CurrentContainers++;
        if ((capability & NodeCapability.Kvm) == NodeCapability.Kvm)
            node.CurrentVms++;
    }

    internal static void ReleaseCapacity(WorkerNode node, NodeCapability capability)
    {
        if ((capability & NodeCapability.Docker) == NodeCapability.Docker)
            node.CurrentContainers = Math.Max(0, node.CurrentContainers - 1);
        if ((capability & NodeCapability.Kvm) == NodeCapability.Kvm)
            node.CurrentVms = Math.Max(0, node.CurrentVms - 1);
    }

    async Task ReleaseReservationAfterAssignmentFailureAsync(FleetCapacityReservationResult reservation,
        CancellationToken token)
    {
        if (reservation.NodeId is not { } nodeId)
            return;

        try
        {
            await _capacity.ReleaseAsync(nodeId, reservation.DockerSlots, reservation.VmSlots, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to release reserved capacity after deployment target assignment failed on node {NodeId}",
                nodeId);
        }
    }

    async Task<DeploymentQueueStatusModel?> TryCreateQueueTicketAsync(DeploymentTarget target, CancellationToken token)
    {
        DeploymentQueueRequest? request = target.Type switch
        {
            TargetType.Docker => TryBuildDockerQueueRequest(target),
            TargetType.Vm => TryBuildVmQueueRequest(target),
            _ => null
        };

        if (request is null)
            return null;

        var result = await _queueService.EnqueueAsync(request, token);
        var ticket = await _context.DeploymentQueueTickets
            .FirstOrDefaultAsync(t => t.Id == result.TicketId, token);
        if (ticket is not null && ticket.DeploymentTargetId is null)
            ticket.DeploymentTargetId = target.Id;

        target.ErrorMessage = $"Waiting in deployment queue. People ahead: {result.PeopleAhead}.";
        await _context.SaveChangesAsync(token);
        return await _queueService.GetStatusAsync(result.TicketId, token);
    }

    static DeploymentQueueRequest? TryBuildDockerQueueRequest(DeploymentTarget target)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ContainerConfig>(target.Payload);
            if (config is null)
                return null;

            if (config.GameId is { } gameId && int.TryParse(config.TeamId, out var teamId))
                return DeploymentQueueRequest.GameContainer(gameId, teamId, config.ChallengeId);

            if (config.TeamId == "exercise")
                return DeploymentQueueRequest.ExerciseContainer(config.UserId, config.ChallengeId);
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    static DeploymentQueueRequest? TryBuildVmQueueRequest(DeploymentTarget target)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<VmQueuePayload>(target.Payload);
            if (payload is null || payload.VmInstanceId is null)
                return null;

            return DeploymentQueueRequest.Vm(
                payload.GameId,
                payload.UserId,
                payload.ChallengeId,
                payload.VmInstanceId.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    sealed record VmQueuePayload(int GameId, Guid UserId, int ChallengeId, Guid? VmInstanceId);
}

public sealed record FleetScheduleResult(
    Guid? NodeId,
    WorkerNode? Node,
    DeploymentTarget? Target,
    bool IsQueued,
    string? Reason,
    DeploymentQueueStatusModel? QueueStatus = null)
{
    public static FleetScheduleResult Scheduled(Guid nodeId, WorkerNode? node, DeploymentTarget target) =>
        new(nodeId, node, target, false, null);

    public static FleetScheduleResult Queued(DeploymentTarget target, string reason,
        DeploymentQueueStatusModel? queueStatus = null) =>
        new(null, null, target, true, reason, queueStatus);

    public static FleetScheduleResult NotScheduled(string reason) =>
        new(null, null, null, false, reason);
}
