using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Container.Manager;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Options;
using DockerManager = GZCTF.Services.Container.Manager.DockerManager;
using IContainerManager = GZCTF.Services.Container.Manager.IContainerManager;
using DataContainer = GZCTF.Models.Data.Container;

namespace GZCTF.Services.Fleet;

public class FleetContainerManager : IContainerManager, IContainerPatchApplicator, IContainerCommandExecutor, IPenetrationFabricManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentClient _agentClient;
    private readonly DockerManager _localManager;
    private readonly IPortAllocationService _portAllocator;
    private readonly ContainerProvider _containerConfig;
    private readonly INginxProxySyncService _nginxProxySync;
    private readonly ILogger<FleetContainerManager> _logger;

    public FleetContainerManager(
        IServiceScopeFactory scopeFactory,
        AgentClient agentClient,
        DockerManager localManager,
        IPortAllocationService portAllocator,
        INginxProxySyncService nginxProxySync,
        IOptions<ContainerProvider> containerConfig,
        ILogger<FleetContainerManager> logger)
    {
        _scopeFactory = scopeFactory;
        _agentClient = agentClient;
        _localManager = localManager;
        _portAllocator = portAllocator;
        _nginxProxySync = nginxProxySync;
        _containerConfig = containerConfig.Value;
        _logger = logger;
    }

    /// <summary>
    /// 是否启用 Nginx 反向代理模式
    /// </summary>
    private bool IsNginxProxyEnabled => _containerConfig.NginxProxyConfig?.Enable == true;

    /// <summary>
    /// 公网入口地址（Nginx 代理模式下的统一入口）
    /// </summary>
    private string PublicEntry => _containerConfig.PublicEntry;

    public async Task<DataContainer?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var fleetManager = scope.ServiceProvider.GetRequiredService<FleetManager>();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queueState = scope.ServiceProvider.GetService<DeploymentQueueStateAccessor>();

        if (config.PreferredNodeId is { } preferredNodeId)
            return await CreateOnPreferredNodeAsync(config, preferredNodeId, nodeRepo, context, token);

        var onlineNodes = await nodeRepo.GetOnlineNodesAsync(token);
        if (onlineNodes.Count == 0)
        {
            _logger.LogWarning("No online fleet node available for Docker container creation");
            _logger.SystemLog("Docker deployment failed: no online fleet node is available.",
                TaskStatus.Failed, LogLevel.Warning);
            return null;
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
                schedule.Target.Status = TargetStatus.Pending;
                schedule.Target.ErrorMessage = schedule.Reason ?? "Waiting for a schedulable fleet node";
                await SaveFleetStateAsync(context, "queue Docker deployment target", token);
                _logger.SystemLogDeploymentTarget("queued", schedule.Target);
            }

            queueState?.SetQueued(schedule.QueueStatus);
            _logger.LogInformation("Docker container creation queued: {Reason}", schedule.Reason);
            return null;
        }

        var node = schedule.Node ?? await nodeRepo.GetNodeByIdAsync(nodeId.Value, token);
        if (schedule.Target is not null)
        {
            schedule.Target.Status = TargetStatus.Creating;
            _logger.SystemLogDeploymentTarget("image-distribution", schedule.Target, node,
                "ensuring Docker image on worker from storage registry");
        }

        if (node?.IsLocal == true)
        {
            var container = await _localManager.CreateContainerAsync(config, token);
            if (container is not null)
            {
                container.NodeId = nodeId.Value;
                if (!await ApplyPublicProxyAsync(container, config, node, schedule.Target, context, token))
                    return null;

                await ConfirmReservedCapacityAsync(context, nodeId.Value, NodeCapability.Docker, config, token);
            }
            else
                ReleaseReservedCapacity(node, NodeCapability.Docker);

            CompleteDeploymentTarget(schedule.Target, container, node.HostAddress);
            await SaveFleetStateAsync(context, "complete scheduled local Docker deployment target", token);
            _logger.SystemLogDeploymentTarget("completed", schedule.Target, node);
            await SyncNginxIfProxiedAsync(container, "scheduled local Docker container created", token);
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
            NetworkName = config.NetworkName,
            IPAddress = config.IPAddress,
            AdditionalNetworkNames = config.AdditionalNetworkNames,
            NetworkSubnets = config.NetworkSubnets,
            NetworkAttachments = config.NetworkAttachments,
            PublishPort = config.PublishPort,
            PreferredHostPort = config.PreferredHostPort,
            BypassPublicProxy = config.BypassPublicProxy,
            EnvironmentVariables = config.EnvironmentVariables,
            StartCommand = config.StartCommand,
            DnsServers = config.DnsServers,
            HealthCheck = config.HealthCheck,
            UsePenetrationFabric = config.UsePenetrationFabric,
            EnableNetworkAdmin = config.EnableNetworkAdmin,
            RemoveDefaultRoute = config.RemoveDefaultRoute,
            EnableIpForwarding = config.EnableIpForwarding,
            PreferredNodeId = config.PreferredNodeId,
            FleetCapacityReserved = config.FleetCapacityReserved
        };
        if (!await EnsureDockerImageReadyAsync(nodeId.Value, remoteConfig.Image, schedule.Target, node, context, token))
        {
            if (node is not null)
                ReleaseReservedCapacity(node, NodeCapability.Docker);
            return null;
        }

        _logger.SystemLogDeploymentTarget("container-creating", schedule.Target, node,
            "Docker image is ready on worker");
        var result = await _agentClient.CreateContainerAsync(nodeId.Value, remoteConfig, token);

        if (result is null)
        {
            _logger.LogWarning("Agent container creation failed on node {NodeId}", nodeId.Value);
            FailDeploymentTarget(schedule.Target, "Agent container creation failed");
            if (node is not null)
                ReleaseReservedCapacity(node, NodeCapability.Docker);

            await SaveFleetStateAsync(context, "fail remote Docker deployment target", token);
            _logger.SystemLogDeploymentTarget("failed", schedule.Target, node);
            return null;
        }

        var remoteContainer = new DataContainer
        {
            ContainerId = result.ContainerId,
            Image = config.Image,
            IP = config.PublishPort ? node!.HostAddress : result.IP,
            Port = config.PublishPort && result.PublicPort > 0 ? result.PublicPort : result.Port,
            PublicIP = config.PublishPort ? node!.HostAddress : null,
            PublicPort = result.PublicPort,
            IsProxy = false,
            Status = ContainerStatus.Running,
            NodeId = nodeId.Value,
        };
        if (!await ApplyPublicProxyAsync(remoteContainer, config, node!, schedule.Target, context, token))
            return null;
        await ConfirmReservedCapacityAsync(context, nodeId.Value, NodeCapability.Docker, config, token);
        CompleteDeploymentTarget(schedule.Target, remoteContainer, node!.HostAddress);
        await SaveFleetStateAsync(context, "complete remote Docker deployment target", token);
        _logger.SystemLogDeploymentTarget("completed", schedule.Target, node);
        await SyncNginxIfProxiedAsync(remoteContainer, "remote Docker container created", token);
        return remoteContainer;
    }

    async Task<DataContainer?> CreateOnPreferredNodeAsync(ContainerConfig config, Guid nodeId,
        INodeRepository nodeRepo, AppDbContext context, CancellationToken token)
    {
        var node = await nodeRepo.GetNodeByIdAsync(nodeId, token);
        var target = new DeploymentTarget
        {
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            TargetNodeId = nodeId,
            Payload = JsonSerializer.Serialize(config),
            Status = TargetStatus.Creating
        };
        context.DeploymentTargets.Add(target);

        var canUseNode = config.FleetCapacityReserved
            ? CanUseReservedDockerCapacity(node)
            : node is not null && WeightedScheduler.CanHost(node, NodeCapability.Docker);

        if (!canUseNode)
        {
            target.Status = TargetStatus.Failed;
            target.CompletedAt = DateTimeOffset.UtcNow;
            target.ErrorMessage = node is null ? "Preferred fleet node not found" : "Preferred fleet node cannot host Docker containers";
            await SaveFleetStateAsync(context, "fail preferred Docker deployment target", token);
            _logger.SystemLogDeploymentTarget("failed", target, node);
            return null;
        }

        var selectedNode = node!;

        if (!config.FleetCapacityReserved)
            FleetManager.ReserveCapacity(selectedNode, NodeCapability.Docker);

        if (selectedNode.IsLocal)
        {
            _logger.SystemLogDeploymentTarget("creating", target, selectedNode);
            var localContainer = await _localManager.CreateContainerAsync(config, token);
            if (localContainer is not null)
            {
                localContainer.NodeId = selectedNode.Id;
                if (!await ApplyPublicProxyAsync(localContainer, config, selectedNode, target, context, token))
                    return null;

                if (!config.FleetCapacityReserved)
                    FleetManager.ConfirmCapacity(selectedNode, NodeCapability.Docker);
            }
            else if (!config.FleetCapacityReserved)
                ReleaseReservedCapacity(selectedNode, NodeCapability.Docker);

            CompleteDeploymentTarget(target, localContainer, selectedNode.HostAddress);
            await SaveFleetStateAsync(context, "complete preferred local Docker deployment target", token);
            _logger.SystemLogDeploymentTarget("completed", target, selectedNode);
            await SyncNginxIfProxiedAsync(localContainer, "preferred local Docker container created", token);
            return localContainer;
        }

        _logger.SystemLogDeploymentTarget("image-distribution", target, selectedNode,
            "ensuring Docker image on worker from storage registry");
        if (!await EnsureDockerImageReadyAsync(selectedNode.Id, config.Image, target, selectedNode, context, token))
        {
            if (!config.FleetCapacityReserved)
                ReleaseReservedCapacity(selectedNode, NodeCapability.Docker);
            return null;
        }

        _logger.SystemLogDeploymentTarget("container-creating", target, selectedNode,
            "Docker image is ready on worker");
        var result = await _agentClient.CreateContainerOrThrowAsync(selectedNode.Id, config, token);
        if (result is null)
        {
            if (!config.FleetCapacityReserved)
                ReleaseReservedCapacity(selectedNode, NodeCapability.Docker);
            FailDeploymentTarget(target, "Agent container creation failed on preferred node");
            await SaveFleetStateAsync(context, "fail preferred remote Docker deployment target", token);
            _logger.SystemLogDeploymentTarget("failed", target, selectedNode);
            return null;
        }

        var remoteContainer = new DataContainer
        {
            ContainerId = result.ContainerId,
            Image = config.Image,
            IP = config.PublishPort ? selectedNode.HostAddress : result.IP,
            Port = config.PublishPort && result.PublicPort > 0 ? result.PublicPort : result.Port,
            PublicIP = config.PublishPort ? selectedNode.HostAddress : null,
            PublicPort = result.PublicPort,
            IsProxy = false,
            Status = ContainerStatus.Running,
            NodeId = selectedNode.Id,
        };
        if (!await ApplyPublicProxyAsync(remoteContainer, config, selectedNode, target, context, token))
            return null;
        if (!config.FleetCapacityReserved)
            FleetManager.ConfirmCapacity(selectedNode, NodeCapability.Docker);
        CompleteDeploymentTarget(target, remoteContainer, selectedNode.HostAddress);
        await SaveFleetStateAsync(context, "complete preferred remote Docker deployment target", token);
        _logger.SystemLogDeploymentTarget("completed", target, selectedNode);
        await SyncNginxIfProxiedAsync(remoteContainer, "preferred remote Docker container created", token);
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
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var node = await nodeRepo.GetNodeByIdAsync(container.NodeId.Value, token);

        if (node?.IsLocal == true)
        {
            await _localManager.DestroyContainerAsync(container, token);
        }
        else
        {
            await _agentClient.DestroyContainerAsync(container.NodeId.Value, container.ContainerId, token);
            container.Status = ContainerStatus.Destroyed;

        }

        // Release only ports allocated from the central Nginx proxy pool.
        if (IsNginxProxyEnabled && container.PublicPort.HasValue &&
            string.Equals(container.PublicIP, PublicEntry, StringComparison.OrdinalIgnoreCase))
            await ReleasePublicPortAsync(container.PublicPort.Value, token);

        if (node is not null && container.Status == ContainerStatus.Destroyed)
        {
            FleetManager.ReleaseCurrentCapacity(node, NodeCapability.Docker);
            await SaveFleetStateAsync(context, "release Docker node capacity after destroy", token);
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

    public bool IsSupported => true;

    public async Task<ContainerCommandResult> ExecuteAsync(DataContainer container, IReadOnlyList<string> command,
        TimeSpan timeout, CancellationToken token = default)
    {
        if (!container.NodeId.HasValue)
            return await _localManager.ExecuteAsync(container, command, timeout, token);

        using var scope = _scopeFactory.CreateScope();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        var node = await nodeRepo.GetNodeByIdAsync(container.NodeId.Value, token);

        if (node?.IsLocal == true)
            return await _localManager.ExecuteAsync(container, command, timeout, token);

        var result = await _agentClient.ExecuteContainerCommandAsync(container.NodeId.Value, container.ContainerId,
            command, (int)Math.Ceiling(timeout.TotalSeconds), token);
        return new ContainerCommandResult(result.IsSupported, result.Succeeded, result.TimedOut,
            result.ExitCode, result.Message);
    }

    public async Task<PenetrationFabricResult> CreateNetworkAsync(string networkName, string cidr,
        CancellationToken token = default)
    {
        return PenetrationFabricResult.Unsupported(
            "Fleet fabric network creation requires a runtime container context; call AttachInterfaceAsync after containers are scheduled.");
    }

    public async Task<PenetrationFabricResult> AttachInterfaceAsync(DataContainer container,
        PenetrationFabricInterfaceSpec spec, CancellationToken token = default)
    {
        var node = await ResolveContainerNode(container, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported("容器没有可解析的 Fleet 节点，不能配置 fabric 网卡。");

        if (node.IsLocal)
        {
            var create = await _localManager.CreateNetworkAsync(spec.NetworkName, spec.NetworkCidr, token);
            return create.Succeeded
                ? await _localManager.AttachInterfaceAsync(container, spec, token)
                : create;
        }

        var network = await _agentClient.CreateFabricNetworkAsync(node.Id, spec.NetworkName, spec.NetworkCidr, token);
        return network.Succeeded
            ? await _agentClient.AttachFabricInterfaceAsync(node.Id, container.ContainerId, spec, token)
            : network;
    }

    public async Task<PenetrationFabricResult> EnableForwardingAsync(DataContainer container,
        CancellationToken token = default)
    {
        var node = await ResolveContainerNode(container, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported("容器没有可解析的 Fleet 节点，不能开启 fabric 转发。");

        return node.IsLocal
            ? await _localManager.EnableForwardingAsync(container, token)
            : await _agentClient.EnableFabricForwardingAsync(node.Id, container.ContainerId, token);
    }

    public async Task<PenetrationFabricResult> ApplyRouteAsync(DataContainer container, string targetCidr,
        string gatewayIp, CancellationToken token = default)
    {
        var node = await ResolveContainerNode(container, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported("容器没有可解析的 Fleet 节点，不能写入 fabric 路由。");

        return node.IsLocal
            ? await _localManager.ApplyRouteAsync(container, targetCidr, gatewayIp, token)
            : await _agentClient.ApplyFabricRouteAsync(node.Id, container.ContainerId, targetCidr, gatewayIp, token);
    }

    public async Task<PenetrationFabricResult> ProbeAsync(DataContainer container, string targetIp,
        CancellationToken token = default)
    {
        var node = await ResolveContainerNode(container, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported("容器没有可解析的 Fleet 节点，不能执行 fabric 探测。");

        return node.IsLocal
            ? await _localManager.ProbeAsync(container, targetIp, token)
            : await _agentClient.ProbeFabricAsync(node.Id, container.ContainerId, targetIp, token);
    }

    public async Task<PenetrationFabricResult> RemoveNetworkAsync(string networkName, CancellationToken token = default)
    {
        var removed = false;
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(n => n.Status == NodeStatus.Online)
            .ToListAsync(token);

        foreach (var node in nodes)
        {
            var result = node.IsLocal
                ? await _localManager.RemoveNetworkAsync(networkName, token)
                : await _agentClient.RemoveFabricNetworkAsync(node.Id, networkName, token);
            removed |= result.Succeeded || !result.IsSupported;
        }

        return removed
            ? PenetrationFabricResult.Success("fabric network cleanup attempted")
            : PenetrationFabricResult.Unsupported("没有可用节点执行 fabric 网络清理。");
    }

    async Task<WorkerNode?> ResolveContainerNode(DataContainer container, CancellationToken token)
    {
        if (!container.NodeId.HasValue)
            return null;

        using var scope = _scopeFactory.CreateScope();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        return await nodeRepo.GetNodeByIdAsync(container.NodeId.Value, token);
    }

    async Task SaveFleetStateAsync(AppDbContext context, string operation, CancellationToken token)
    {
        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                await context.SaveChangesAsync(token);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is WorkerNode))
            {
                _logger.LogWarning(ex,
                    "Worker node state changed while trying to {Operation}; saving deployment state without stale node counters.",
                    operation);

                foreach (var entry in ex.Entries)
                    ResolveWorkerNodeConcurrencyEntry(entry);
            }
        }

        await context.SaveChangesAsync(token);
    }

    async Task<bool> EnsureDockerImageReadyAsync(Guid nodeId, string image, DeploymentTarget? target,
        WorkerNode? node, AppDbContext context, CancellationToken token)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var distribution = scope.ServiceProvider.GetRequiredService<ImageDistributionService>();
            await distribution.EnsureDockerImageOnNodeAsync(image, nodeId, token);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or AgentClientException or HttpRequestException)
        {
            var nodeName = node?.Name ?? nodeId.ToString();
            var message = $"Node {nodeName} failed to ensure Docker image {image} from storage registry: {ex.Message}";
            _logger.LogWarning(ex, "Failed to ensure Docker image {Image} on node {NodeId}", image, nodeId);
            FailDeploymentTarget(target, message);
            await SaveFleetStateAsync(context, "fail Docker deployment image distribution", token);
            _logger.SystemLogDeploymentTarget("failed", target, node, message);
            return false;
        }
    }

    static void ResolveWorkerNodeConcurrencyEntry(EntityEntry entry)
    {
        if (entry.Entity is WorkerNode)
        {
            entry.State = EntityState.Detached;
            return;
        }

        throw new InvalidOperationException("Unexpected non-worker concurrency conflict while saving fleet state.");
    }

    static void ReleaseReservedCapacity(WorkerNode node, NodeCapability capability) =>
        FleetManager.ReleaseCapacity(node, capability);

    static async Task ConfirmReservedCapacityAsync(AppDbContext context, Guid nodeId, NodeCapability capability,
        ContainerConfig config, CancellationToken token)
    {
        if (config.FleetCapacityReserved)
            return;

        var node = await context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == nodeId, token);
        if (node is null)
            return;

        FleetManager.ConfirmCapacity(node, capability);
    }

    /// <summary>
    /// 通过 PortAllocationService 分配公网端口（Nginx 代理模式）
    /// </summary>
    async Task<int?> AllocatePublicPortAsync(Guid containerId, CancellationToken token)
    {
        try
        {
            var port = await _portAllocator.AllocatePortAsync(containerId, token);
            if (port == 0)
            {
                _logger.LogError("Port allocation failed: no available port in range");
                return null;
            }
            return port;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Port allocation failed for container {ContainerId}", containerId);
            return null;
        }
    }

    static Guid ParseContainerGuid(string containerId)
    {
        if (Guid.TryParse(containerId, out var parsed))
            return parsed;

        return Guid.CreateVersion7();
    }

    async Task<bool> ApplyPublicProxyAsync(DataContainer container, ContainerConfig config, WorkerNode node,
        DeploymentTarget? target, AppDbContext context, CancellationToken token)
    {
        if (!ShouldUsePublicProxy(config, node))
            return true;

        if (container.PublicPort is not > 0)
        {
            await CleanupContainerAfterProxyAllocationFailure(container, node, config, target, context, token,
                "Container did not expose a worker host port for Nginx proxy");
            return false;
        }

        var workerHostPort = container.PublicPort.Value;
        var proxyPort = await AllocatePublicPortAsync(ParseContainerGuid(container.ContainerId), token);
        if (proxyPort is null)
        {
            await CleanupContainerAfterProxyAllocationFailure(container, node, config, target, context, token,
                "No available Nginx proxy public port");
            return false;
        }

        container.IP = node.HostAddress;
        container.Port = workerHostPort;
        container.PublicIP = PublicEntry;
        container.PublicPort = proxyPort.Value;
        return true;
    }

    bool ShouldUsePublicProxy(ContainerConfig config, WorkerNode node)
    {
        if (!IsNginxProxyEnabled || !config.PublishPort || config.BypassPublicProxy ||
            string.IsNullOrWhiteSpace(PublicEntry))
            return false;

        // PublicEntry is the player-facing gateway. The worker Docker host port
        // remains an upstream only, even when the container is created on the
        // main/internal server itself.
        return true;
    }

    Task SyncNginxIfProxiedAsync(DataContainer? container, string reason, CancellationToken token)
    {
        if (container is null ||
            !IsNginxProxyEnabled ||
            !string.Equals(container.PublicIP, PublicEntry, StringComparison.OrdinalIgnoreCase) ||
            container.PublicPort is not > 0)
            return Task.CompletedTask;

        return _nginxProxySync.TrySyncNowAsync(reason, token);
    }

    /// <summary>
    /// 通过 PortAllocationService 释放公网端口（Nginx 代理模式）
    /// </summary>
    async Task ReleasePublicPortAsync(int port, CancellationToken token)
    {
        try
        {
            await _portAllocator.ReleasePortAsync(port, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Port release failed for port {Port}", port);
        }
    }

    async Task CleanupContainerAfterProxyAllocationFailure(DataContainer container, WorkerNode node,
        ContainerConfig config, DeploymentTarget? target, AppDbContext context, CancellationToken token, string reason)
    {
        _logger.LogError("Nginx proxy setup failed for container {ContainerId} on node {NodeId}: {Reason}",
            container.ContainerId, node.Id, reason);
        try
        {
            if (node.IsLocal)
                await _localManager.DestroyContainerAsync(container, token);
            else
                await _agentClient.DestroyContainerAsync(node.Id, container.ContainerId, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to destroy container {ContainerId} after Nginx proxy setup failure",
                container.ContainerId);
        }

        if (!config.FleetCapacityReserved && target is not null)
            ReleaseReservedCapacity(node, NodeCapability.Docker);

        FailDeploymentTarget(target, reason);
        await SaveFleetStateAsync(context, "fail Docker deployment after Nginx proxy setup", token);
        _logger.SystemLogDeploymentTarget("failed", target, node);
    }

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

    internal static bool CanUseReservedDockerCapacity(WorkerNode? node)
    {
        if (node is null)
            return false;

        return node.GetEffectiveStatus(DateTimeOffset.UtcNow) == NodeStatus.Online
            && node.IsSchedulable
            && (node.Capabilities & NodeCapability.Docker) == NodeCapability.Docker
            && node.AllocatedContainers < node.MaxContainers;
    }
}
