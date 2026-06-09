using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Container.Manager;
using Microsoft.EntityFrameworkCore;
using DockerManager = GZCTF.Services.Container.Manager.DockerManager;
using IContainerManager = GZCTF.Services.Container.Manager.IContainerManager;
using DataContainer = GZCTF.Models.Data.Container;

namespace GZCTF.Services.Fleet;

public class FleetContainerManager : IContainerManager, IContainerPatchApplicator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentClient _agentClient;
    private readonly DockerManager _localManager;
    private readonly ILogger<FleetContainerManager> _logger;

    public FleetContainerManager(
        IServiceScopeFactory scopeFactory,
        AgentClient agentClient,
        DockerManager localManager,
        ILogger<FleetContainerManager> logger)
    {
        _scopeFactory = scopeFactory;
        _agentClient = agentClient;
        _localManager = localManager;
        _logger = logger;
    }

    public async Task<DataContainer?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var fleetManager = scope.ServiceProvider.GetRequiredService<FleetManager>();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var onlineNodes = await nodeRepo.GetOnlineNodesAsync(token);
        if (onlineNodes.Count == 0)
        {
            _logger.LogInformation("No online fleet node available, falling back to local Docker manager");
            return await _localManager.CreateContainerAsync(config, token);
        }

        var target = new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Payload = JsonSerializer.Serialize(config)
        };
        var schedule = await fleetManager.TryScheduleWithTargetAsync(target, token);
        var nodeId = schedule.NodeId;

        if (nodeId is null)
        {
            if (schedule.Target is not null)
            {
                schedule.Target.Status = TargetStatus.Cancelled;
                schedule.Target.CompletedAt = DateTimeOffset.UtcNow;
                schedule.Target.ErrorMessage = "No schedulable fleet node; handled by local Docker fallback";
                await context.SaveChangesAsync(token);
            }

            _logger.LogWarning("No schedulable fleet node available, falling back to local Docker manager");
            return await _localManager.CreateContainerAsync(config, token);
        }

        var node = schedule.Node ?? await nodeRepo.GetNodeByIdAsync(nodeId.Value, token);

        if (node?.IsLocal == true)
        {
            var container = await _localManager.CreateContainerAsync(config, token);
            if (container is not null)
                container.NodeId = nodeId.Value;
            else
                ReleaseReservedCapacity(node, NodeCapability.Docker);
            CompleteDeploymentTarget(schedule.Target, container, node.HostAddress);
            await context.SaveChangesAsync(token);
            return container;
        }

        var remoteConfig = new ContainerConfig
        {
            Image = config.Image,
            TeamId = config.TeamId,
            ChallengeId = config.ChallengeId,
            UserId = config.UserId,
            ExposedPort = config.ExposedPort,
            Flag = config.Flag,
            EnableTrafficCapture = config.EnableTrafficCapture,
            MemoryLimit = config.MemoryLimit,
            CPUCount = config.CPUCount,
            StorageLimit = config.StorageLimit,
            NetworkMode = config.NetworkMode,
        };
        var result = await _agentClient.CreateContainerAsync(nodeId.Value, remoteConfig, token);

        if (result is null)
        {
            _logger.LogWarning("Agent container creation failed on node {NodeId}", nodeId.Value);
            FailDeploymentTarget(schedule.Target, "Agent container creation failed");
            if (node is not null)
                ReleaseReservedCapacity(node, NodeCapability.Docker);

            var fallback = await TryCreateLocalFallback(config, schedule.Target, context, token);
            await context.SaveChangesAsync(token);
            return fallback;
        }

        var remoteContainer = new DataContainer
        {
            ContainerId = result.ContainerId,
            Image = config.Image,
            IP = result.IP,
            Port = result.Port,
            PublicIP = node!.HostAddress,
            PublicPort = result.PublicPort,
            IsProxy = false,
            Status = ContainerStatus.Running,
            NodeId = nodeId.Value,
        };
        CompleteDeploymentTarget(schedule.Target, remoteContainer, node!.HostAddress);
        await context.SaveChangesAsync(token);
        return remoteContainer;
    }

    public async Task DestroyContainerAsync(DataContainer container, CancellationToken token = default)
    {
        if (!container.NodeId.HasValue)
        {
            await _localManager.DestroyContainerAsync(container, token);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        var node = await nodeRepo.GetNodeByIdAsync(container.NodeId.Value, token);

        if (node?.IsLocal == true)
        {
            await _localManager.DestroyContainerAsync(container, token);
        }
        else
        {
            try
            {
                await _agentClient.DestroyContainerAsync(container.NodeId.Value, container.ContainerId, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent container destruction failed for {ContainerId}", container.ContainerId);
            }
            container.Status = ContainerStatus.Destroyed;
        }
    }

    public async Task<ContainerPatchApplyResult> ApplyPatchAsync(DataContainer container, Stream archive,
        CancellationToken token = default)
    {
        if (!container.NodeId.HasValue)
            return await _localManager.ApplyPatchAsync(container, archive, token);

        using var scope = _scopeFactory.CreateScope();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        var node = await nodeRepo.GetNodeByIdAsync(container.NodeId.Value, token);

        return node?.IsLocal == true
            ? await _localManager.ApplyPatchAsync(container, archive, token)
            : ContainerPatchApplyResult.Unsupported("Remote fleet node patch application is not supported");
    }

    async Task<DataContainer?> TryCreateLocalFallback(ContainerConfig config, DeploymentTarget? target,
        AppDbContext context, CancellationToken token)
    {
        var localNode = await context.WorkerNodes
            .FirstOrDefaultAsync(n => n.IsLocal && n.Status == NodeStatus.Online && n.IsSchedulable, token);

        if (localNode is null || !WeightedScheduler.CanHost(localNode, NodeCapability.Docker))
            return null;

        FleetManager.ReserveCapacity(localNode, NodeCapability.Docker);
        _logger.LogInformation("Falling back to local Docker manager after remote container creation failure");
        var container = await _localManager.CreateContainerAsync(config, token);
        if (container is null)
        {
            ReleaseReservedCapacity(localNode, NodeCapability.Docker);
            return null;
        }

        container.NodeId = localNode.Id;
        target ??= new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Payload = JsonSerializer.Serialize(config)
        };
        if (context.Entry(target).State == EntityState.Detached)
            context.DeploymentTargets.Add(target);

        target.TargetNodeId = localNode.Id;
        target.Status = TargetStatus.Completed;
        target.ResultHost = container.PublicIP ?? localNode.HostAddress;
        target.ResultPort = container.PublicPort ?? container.Port;
        target.CompletedAt = DateTimeOffset.UtcNow;
        target.ErrorMessage = "Remote agent failed; completed by local Docker fallback";
        return container;
    }

    static void ReleaseReservedCapacity(WorkerNode node, NodeCapability capability) =>
        FleetManager.ReleaseCapacity(node, capability);

    static void CompleteDeploymentTarget(DeploymentTarget? target, DataContainer? container, string? host)
    {
        if (target is null)
            return;

        target.CompletedAt = DateTimeOffset.UtcNow;
        if (container is null)
        {
            target.Status = TargetStatus.Failed;
            target.ErrorMessage = "Container creation failed";
            return;
        }

        target.Status = TargetStatus.Completed;
        target.ResultHost = container.PublicIP ?? host ?? container.IP;
        target.ResultPort = container.PublicPort ?? container.Port;
        target.ErrorMessage = null;
    }

    static void FailDeploymentTarget(DeploymentTarget? target, string message)
    {
        if (target is null)
            return;

        target.Status = TargetStatus.Failed;
        target.CompletedAt = DateTimeOffset.UtcNow;
        target.ErrorMessage = message;
    }
}
