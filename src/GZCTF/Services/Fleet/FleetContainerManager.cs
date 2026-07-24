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
        if (config.PreferredNodeId is not { } nodeId)
        {
            _logger.LogError(
                "Fleet container execution requires a scheduler-assigned node for image {Image}.",
                config.Image);
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var execution = scope.ServiceProvider.GetService<DeploymentExecutionContextAccessor>();
        return await CreateOnPreferredNodeAsync(config, nodeId, nodeRepo, context,
            execution?.Current?.TicketId, token);
    }

    async Task<DataContainer?> CreateOnPreferredNodeAsync(ContainerConfig config, Guid nodeId,
        INodeRepository nodeRepo, AppDbContext context, Guid? ticketId, CancellationToken token)
    {
        var node = await nodeRepo.GetNodeByIdAsync(nodeId, token);

        var canUseNode = config.FleetCapacityReserved && CanUseReservedDockerCapacity(node);

        if (!canUseNode)
        {
            var message = node is null
                ? "Preferred fleet node not found"
                : "Preferred fleet node cannot host Docker containers";
            await UpdateTicketStageAsync(context, ticketId, DeploymentStage.Failed, message, token);
            return null;
        }

        var selectedNode = node!;

        if (selectedNode.IsLocal)
        {
            await UpdateTicketStageAsync(context, ticketId, DeploymentStage.ContainerCreating,
                "Creating container on local Docker node.", token);
            var localContainer = await _localManager.CreateContainerAsync(config, token);
            if (localContainer is not null)
            {
                localContainer.NodeId = selectedNode.Id;
                if (!await ApplyPublicProxyAsync(localContainer, config, selectedNode, ticketId, context, token))
                    return null;

            }

            await UpdateTicketStageAsync(context, ticketId,
                localContainer is null ? DeploymentStage.Failed : DeploymentStage.BootProbing,
                localContainer is null ? "Local container creation failed." : "Container started; probing service.", token);
            await SyncNginxIfProxiedAsync(localContainer, "preferred local Docker container created", token);
            return localContainer;
        }

        await UpdateTicketStageAsync(context, ticketId, DeploymentStage.ImagePreparing,
            "Ensuring Docker image on worker from storage registry.", token);
        if (!await EnsureDockerImageReadyAsync(selectedNode.Id, config.Image, ticketId, selectedNode, context, token))
        {
            return null;
        }

        await UpdateTicketStageAsync(context, ticketId, DeploymentStage.ContainerCreating,
            "Docker image is ready; creating container.", token);
        var result = await _agentClient.CreateContainerOrThrowAsync(selectedNode.Id, config, token);
        if (result is null)
        {
            await UpdateTicketStageAsync(context, ticketId, DeploymentStage.Failed,
                "Agent container creation failed on preferred node.", token);
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
            RuntimeGeneration = Math.Max(1, config.Generation),
        };
        if (!await ApplyPublicProxyAsync(remoteContainer, config, selectedNode, ticketId, context, token))
            return null;
        await UpdateTicketStageAsync(context, ticketId, DeploymentStage.BootProbing,
            "Container started; probing service.", token);
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
            await _agentClient.DestroyContainerAsync(
                container.NodeId.Value, container.ContainerId, container.RuntimeGeneration, token);
            container.Status = ContainerStatus.Destroyed;

        }

        // Release only ports allocated from the central Nginx proxy pool.
        if (IsNginxProxyEnabled && container.PublicPort.HasValue &&
            string.Equals(container.PublicIP, PublicEntry, StringComparison.OrdinalIgnoreCase))
            await ReleasePublicPortAsync(container.PublicPort.Value, container.PublicPortLeaseId, token);

        if (node is not null && container.Status == ContainerStatus.Destroyed)
        {
            node.CurrentContainers = Math.Max(0, node.CurrentContainers - 1);
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

    async Task<bool> EnsureDockerImageReadyAsync(Guid nodeId, string image, Guid? ticketId,
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
            await UpdateTicketStageAsync(context, ticketId, DeploymentStage.Failed, message, token);
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

    /// <summary>
    /// 通过 PortAllocationService 分配公网端口（Nginx 代理模式）
    /// </summary>
    async Task<PortLease?> AllocatePublicPortAsync(Guid containerId, CancellationToken token)
    {
        try
        {
            var lease = await _portAllocator.AllocatePortAsync(containerId, token);
            if (lease is null)
            {
                _logger.LogError("Port allocation failed: no available port in range");
                return null;
            }
            return lease;
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
        Guid? ticketId, AppDbContext context, CancellationToken token)
    {
        if (!ShouldUsePublicProxy(config, node))
            return true;

        if (container.PublicPort is not > 0)
        {
            await CleanupContainerAfterProxyAllocationFailure(container, node, config, ticketId, context, token,
                "Container did not expose a worker host port for Nginx proxy");
            return false;
        }

        var workerHostPort = container.PublicPort.Value;
        var proxyPort = await AllocatePublicPortAsync(ParseContainerGuid(container.ContainerId), token);
        if (proxyPort is null)
        {
            await CleanupContainerAfterProxyAllocationFailure(container, node, config, ticketId, context, token,
                "No available Nginx proxy public port");
            return false;
        }

        container.IP = node.HostAddress;
        container.Port = workerHostPort;
        container.PublicIP = PublicEntry;
        container.PublicPort = proxyPort.Port;
        container.PublicPortLeaseId = proxyPort.LeaseId;
        container.EntryStatus = ContainerEntryStatus.Pending;
        container.EntryReadyAt = null;
        container.EntryError = null;
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
    async Task ReleasePublicPortAsync(int port, Guid? leaseId, CancellationToken token)
    {
        try
        {
            if (leaseId is null)
            {
                _logger.LogWarning("Port {Port} has no owner lease identity and will not be released unsafely", port);
                return;
            }
            await _portAllocator.ReleasePortAsync(port, leaseId.Value, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Port release failed for port {Port}", port);
        }
    }

    async Task CleanupContainerAfterProxyAllocationFailure(DataContainer container, WorkerNode node,
        ContainerConfig config, Guid? ticketId, AppDbContext context, CancellationToken token, string reason)
    {
        _logger.LogError("Nginx proxy setup failed for container {ContainerId} on node {NodeId}: {Reason}",
            container.ContainerId, node.Id, reason);
        try
        {
            if (node.IsLocal)
                await _localManager.DestroyContainerAsync(container, token);
            else
                await _agentClient.DestroyContainerAsync(
                    node.Id, container.ContainerId, config.Generation, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to destroy container {ContainerId} after Nginx proxy setup failure",
                container.ContainerId);
        }

        await UpdateTicketStageAsync(context, ticketId, DeploymentStage.Failed, reason, token);
    }

    static async Task UpdateTicketStageAsync(AppDbContext context, Guid? ticketId, DeploymentStage stage,
        string message, CancellationToken token)
    {
        if (ticketId is null)
            return;
        var ticket = await context.DeploymentQueueTickets.FirstOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket is null)
            return;
        ticket.Stage = stage;
        ticket.StageMessage = message;
        if (stage == DeploymentStage.Failed)
            ticket.ErrorMessage = message;
        await context.SaveChangesAsync(token);
    }

    internal static bool CanUseReservedDockerCapacity(WorkerNode? node)
    {
        if (node is null)
            return false;

        return node.GetEffectiveStatus(DateTimeOffset.UtcNow) == NodeStatus.Online
            && node.IsSchedulable
            && (node.Capabilities & NodeCapability.Docker) == NodeCapability.Docker;
    }
}
