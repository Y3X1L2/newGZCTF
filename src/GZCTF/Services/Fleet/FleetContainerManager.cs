using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using DockerManager = GZCTF.Services.Container.Manager.DockerManager;
using IContainerManager = GZCTF.Services.Container.Manager.IContainerManager;
using DataContainer = GZCTF.Models.Data.Container;

namespace GZCTF.Services.Fleet;

public class FleetContainerManager : IContainerManager
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

        var target = new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            Payload = JsonSerializer.Serialize(config)
        };
        var nodeId = await fleetManager.TryScheduleAsync(target, token);

        if (nodeId is null)
        {
            _logger.LogWarning("No schedulable node available, container creation queued");
            return null;
        }

        var node = await nodeRepo.GetNodeByIdAsync(nodeId.Value, token);

        if (node?.IsLocal == true)
        {
            var container = await _localManager.CreateContainerAsync(config, token);
            if (container is not null)
                container.NodeId = nodeId.Value;
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
            return null;
        }

        return new DataContainer
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
}
