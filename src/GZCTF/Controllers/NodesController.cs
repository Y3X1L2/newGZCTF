using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Admin;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/v1/nodes")]
[Produces(MediaTypeNames.Application.Json)]
public class NodesController : ControllerBase
{
    private readonly INodeRepository _nodeRepo;
    private readonly AppDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ContainerProvider _containerProvider;
    private readonly IPortAllocationService _portAllocator;
    private readonly ILogger<NodesController> _logger;

    public NodesController(INodeRepository nodeRepo, AppDbContext context, IServiceScopeFactory scopeFactory,
        IOptions<ContainerProvider> containerProvider,
        IPortAllocationService portAllocator,
        ILogger<NodesController> logger)
    {
        _nodeRepo = nodeRepo;
        _context = context;
        _scopeFactory = scopeFactory;
        _containerProvider = containerProvider.Value;
        _portAllocator = portAllocator;
        _logger = logger;
    }

    [HttpPost]
    [RequireAdmin]
    public async Task<IActionResult> Register([FromBody] NodeDeployRequest request)
    {
        var deployer = HttpContext.RequestServices.GetRequiredService<NodeDeployService>();
        var requestBaseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var result = await deployer.DeployToServerAsync(
            request.HostAddress, request.Username, request.Password,
            request.NodeName, HttpContext.RequestAborted, requestBaseUrl);

        if (!result.Success)
        {
            _logger.SystemLog($"Worker node registration failed: host={request.HostAddress}, message={result.Message}.",
                TaskStatus.Failed, LogLevel.Warning);
            return BadRequest(new { message = result.Message });
        }

        _logger.SystemLog(
            $"Worker node registered or updated: node={result.NodeName ?? request.NodeName ?? request.HostAddress}, id={result.NodeId}, host={request.HostAddress}, capabilities={result.Capabilities}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok(result);
    }

    [HttpGet]
    [RequireAdmin]
    public async Task<IActionResult> List()
    {
        var nodes = await _nodeRepo.GetAllNodesAsync(HttpContext.RequestAborted);
        var now = DateTimeOffset.UtcNow;
        var portPool = GetPublicPortPool();
        var publicPortUsage = await GetPublicPortUsageAsync(portPool, HttpContext.RequestAborted);
        return Ok(nodes.Select(n => new
        {
            n.Id, n.Name, n.HostAddress, Status = n.GetEffectiveStatus(now), n.Capabilities,
            n.CpuLoad, n.MemoryLoad, n.CurrentContainers, n.MaxContainers,
            n.ReservedContainers,
            AllocatedContainers = n.AllocatedContainers,
            n.CurrentVms, n.MaxVms,
            n.ReservedVms,
            AllocatedVms = n.AllocatedVms,
            UsedPorts = publicPortUsage,
            TotalPorts = portPool.Total,
            PortPoolStart = portPool.Start,
            PortPoolEnd = portPool.End,
            PortPoolMode = portPool.Mode,
            n.LastHeartbeat,
            n.IsSchedulable, n.IsLocal, n.AgentPort,
            n.TeamLabNetworkEnabled,
            n.TeamLabTunnelStatus,
            n.TeamLabTunnelIp,
            n.TeamLabTunnelLastHandshake,
            n.TeamLabTunnelLastError,
            n.TeamLabTunnelConfigVersion,
            n.TeamLabAgentVersion,
            n.TeamLabProtocolVersion,
            n.TeamLabFabricIp,
            n.TeamLabFabricStatus,
            n.TeamLabCapabilitiesJson,
            CanHostTeamLab = WeightedScheduler.CanHostTeamLab(n),
            CanHostTeamLabFabric = WeightedScheduler.CanHostTeamLabFabric(n),
            CanHostTeamLabDocker = WeightedScheduler.CanHostTeamLabDocker(n),
            CanHostTeamLabVm = WeightedScheduler.CanHostTeamLabVm(n),
            UnschedulableReasons = GetUnschedulableReasons(n),
            UnschedulableByCapability = GetUnschedulableByCapability(n),
            SchedulableCapabilities = GetSchedulableCapabilities(n)
        }));
    }

    [HttpGet("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> Detail(Guid id)
    {
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();
        var now = DateTimeOffset.UtcNow;
        var portPool = GetPublicPortPool();
        var publicPortUsage = await GetPublicPortUsageAsync(portPool, HttpContext.RequestAborted);
        return Ok(new
        {
            node.Id, node.Name, node.HostAddress, Status = node.GetEffectiveStatus(now), node.Capabilities,
            node.CpuLoad, node.MemoryLoad, node.CurrentContainers, node.MaxContainers,
            node.ReservedContainers,
            AllocatedContainers = node.AllocatedContainers,
            node.CurrentVms, node.MaxVms,
            node.ReservedVms,
            AllocatedVms = node.AllocatedVms,
            UsedPorts = publicPortUsage,
            TotalPorts = portPool.Total,
            PortPoolStart = portPool.Start,
            PortPoolEnd = portPool.End,
            PortPoolMode = portPool.Mode,
            node.LastHeartbeat,
            node.IsSchedulable, node.IsLocal, node.AgentPort,
            node.TeamLabNetworkEnabled,
            node.TeamLabTunnelStatus,
            node.TeamLabTunnelIp,
            node.TeamLabTunnelLastHandshake,
            node.TeamLabTunnelLastError,
            node.TeamLabTunnelConfigVersion,
            node.TeamLabAgentVersion,
            node.TeamLabProtocolVersion,
            node.TeamLabFabricIp,
            node.TeamLabFabricStatus,
            node.TeamLabCapabilitiesJson,
            CanHostTeamLab = WeightedScheduler.CanHostTeamLab(node),
            CanHostTeamLabFabric = WeightedScheduler.CanHostTeamLabFabric(node),
            CanHostTeamLabDocker = WeightedScheduler.CanHostTeamLabDocker(node),
            CanHostTeamLabVm = WeightedScheduler.CanHostTeamLabVm(node),
            UnschedulableReasons = GetUnschedulableReasons(node),
            UnschedulableByCapability = GetUnschedulableByCapability(node),
            SchedulableCapabilities = GetSchedulableCapabilities(node)
        });
    }

    [HttpGet("{id:guid}/resources")]
    [RequireAdmin]
    public async Task<IActionResult> Resources(
        Guid id,
        [FromQuery] string type = "all",
        [FromQuery] string status = "all",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
    {
        var token = HttpContext.RequestAborted;
        var node = await _context.WorkerNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, token);
        if (node is null) return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var normalizedType = type.Trim().ToLowerInvariant();
        var normalizedStatus = status.Trim().ToLowerInvariant();
        var includeContainers = normalizedType is "all" or "container" or "containers";
        var includeVms = normalizedType is "all" or "vm" or "vms";
        var includePentest = normalizedType is "all" or "pentest" or "penetration";
        var includeTeamLab = normalizedType is "all" or "teamlab";

        var resources = new List<NodeResourceItemModel>();

        if (includeContainers)
        {
            var containers = await _context.Containers.AsNoTracking()
                .Where(c => c.NodeId == id)
                .Include(c => c.GameInstance).ThenInclude(i => i!.Challenge).ThenInclude(c => c.Game)
                .Include(c => c.GameInstance).ThenInclude(i => i!.Participation).ThenInclude(p => p.Team)
                .ToListAsync(token);

            resources.AddRange(containers.Select(ToNodeContainerResource));
        }

        if (includeVms)
        {
            var vms = await _context.VmInstances.AsNoTracking()
                .Where(v => v.NodeId == id)
                .Include(v => v.Challenge).ThenInclude(c => c!.Game)
                .ToListAsync(token);

            var vmUserIds = vms.Select(v => v.UserId).Distinct().ToArray();
            var users = await _context.Users.AsNoTracking()
                .Where(u => vmUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName })
                .ToDictionaryAsync(u => u.Id, u => u.UserName, token);
            var teamRows = await _context.UserParticipations.AsNoTracking()
                .Where(m => vmUserIds.Contains(m.UserId))
                .Include(m => m.Team)
                .ToListAsync(token);
            var teamsByUser = teamRows
                .GroupBy(m => (m.UserId, m.GameId))
                .ToDictionary(g => g.Key, g => g.First().Team);

            var guacService = HttpContext.RequestServices.GetService<GuacamoleService>();
            var vmResources = new List<NodeResourceItemModel>(vms.Count);
            foreach (var vm in vms)
            {
                var entry = await ResolveVmEntryAsync(vm, guacService, token);
                vmResources.Add(ToNodeVmResource(vm, users, teamsByUser, entry));
            }

            resources.AddRange(vmResources);
        }

        if (includePentest || includeTeamLab)
        {
            var assets = await _context.TeamLabRuntimeAssets.AsNoTracking()
                .Where(a => a.WorkerNodeId == id || (a.WorkerNodeId == null && a.Shard != null && a.Shard.WorkerNodeId == id))
                .Include(a => a.Runtime)
                .Include(a => a.Shard)
                .ToListAsync(token);
            var runtimeIds = assets.Select(asset => asset.RuntimeId).Distinct().ToArray();
            var bindings = await (
                    from binding in _context.PenetrationTeamRuntimeBindings.AsNoTracking()
                    join game in _context.Games.AsNoTracking() on binding.GameId equals game.Id
                    join team in _context.Teams.AsNoTracking() on binding.TeamId equals team.Id
                    where runtimeIds.Contains(binding.RuntimeId)
                    select new NodeTeamLabBindingView(
                        binding.RuntimeId, binding.GameId, game.Title, binding.TeamId, team.Name))
                .ToDictionaryAsync(item => item.RuntimeId, token);

            foreach (var asset in assets)
            {
                bindings.TryGetValue(asset.RuntimeId, out var binding);
                if ((binding is null && includeTeamLab) || (binding is not null && includePentest))
                    resources.Add(ToNodeTeamLabResource(asset, binding));
            }
        }

        if (normalizedStatus == "active")
            resources = resources.Where(r => r.IsActive).ToList();
        else if (normalizedStatus == "history")
            resources = resources.Where(r => !r.IsActive).ToList();

        var ordered = resources
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => r.StartedAt)
            .ThenBy(r => r.Kind)
            .ToList();

        var total = ordered.Count;
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new NodeResourceListResponse
        {
            NodeId = node.Id,
            NodeName = node.Name,
            Page = page,
            PageSize = pageSize,
            Total = total,
            RunningCount = resources.Count(r => r.IsActive),
            ContainerCount = resources.Count(r => r.Kind == "container"),
            VmCount = resources.Count(r => r.Kind == "vm"),
            PentestCount = resources.Count(r => r.Kind == "pentest"),
            TeamLabCount = resources.Count(r => r.Kind == "teamlab"),
            Items = items
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> Deregister(Guid id, [FromQuery] bool force = false)
    {
        var token = HttpContext?.RequestAborted ?? CancellationToken.None;
        var node = await _nodeRepo.GetNodeByIdAsync(id, token);
        if (node is null) return NotFound();
        if (node.IsLocal) return BadRequest(new { message = "Cannot deregister local node" });

        if (force)
        {
            node.IsSchedulable = false;
            await _context.SaveChangesAsync(token);

            var cleanup = await ForceCleanupNodeResources(id, token);
            if (!cleanup.Success)
                return BadRequest(new
                {
                    message = cleanup.Message,
                    cleanup.ActiveContainers,
                    cleanup.ActiveVms,
                    cleanup.ActiveTeamLabRuntimes
                });

            node = await _nodeRepo.GetNodeByIdAsync(id, token);
            if (node is null) return NotFound();
        }

        var activeContainers = await _context.Containers.AsNoTracking()
            .CountAsync(c => c.NodeId == id && c.Status != ContainerStatus.Destroyed, token);
        var activeVms = await _context.VmInstances.AsNoTracking()
            .CountAsync(v => v.NodeId == id &&
                             (v.Status == VmInstanceStatus.Creating ||
                              v.Status == VmInstanceStatus.Running), token);
        var activeTeamLabRuntimes = await CountActiveTeamLabRuntimesAsync(id, token);

        if (activeContainers > 0 || activeVms > 0 || activeTeamLabRuntimes > 0)
            return BadRequest(new
            {
                message = "该节点仍承载运行资源，请先停止或清理后再注销。",
                activeContainers,
                activeVms,
                activeTeamLabRuntimes
            });

        await using var transaction = await _context.Database.BeginTransactionAsync(token);

        var now = DateTimeOffset.UtcNow;
        var targets = await _context.DeploymentTargets
            .Where(t => t.TargetNodeId == id)
            .ToListAsync(token);
        foreach (var target in targets)
        {
            if (target.Status is TargetStatus.Pending or TargetStatus.Assigned or TargetStatus.Creating
                or TargetStatus.Running)
            {
                target.Status = TargetStatus.Cancelled;
                target.CompletedAt = now;
                target.ErrorMessage = "Target node was deregistered.";
            }
            target.TargetNodeId = null;
        }

        var tickets = await _context.DeploymentQueueTickets
            .Where(t => t.TargetNodeId == id)
            .ToListAsync(token);
        foreach (var ticket in tickets)
        {
            if (ticket.Status is DeploymentQueueTicketStatus.Pending or DeploymentQueueTicketStatus.Assigned
                or DeploymentQueueTicketStatus.Creating)
            {
                ticket.Status = DeploymentQueueTicketStatus.Cancelled;
                ticket.CompletedAt = now;
                ticket.ErrorMessage = "Target node was deregistered.";
            }
            ticket.TargetNodeId = null;
        }

        foreach (var container in await _context.Containers.Where(c => c.NodeId == id).ToListAsync(token))
            container.NodeId = null;

        foreach (var vm in await _context.VmInstances.Where(v => v.NodeId == id).ToListAsync(token))
            vm.NodeId = null;

        _context.WorkerNodes.Remove(node);
        await _context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        _logger.SystemLog($"Worker node deregistered: node={node.Name}, id={node.Id}, host={node.HostAddress}, force={force}.",
            TaskStatus.Success, LogLevel.Information);

        return NoContent();
    }

    async Task<NodeForceCleanupResult> ForceCleanupNodeResources(Guid nodeId, CancellationToken token)
    {
        var errors = new List<string>();
        var runtimes = HttpContext.RequestServices.GetRequiredService<GZCTF.Modules.TeamLab.Application.ITeamLabRuntimeApplicationService>();
        var containerManager = HttpContext.RequestServices.GetRequiredService<IContainerManager>();
        var fleetVm = HttpContext.RequestServices.GetRequiredService<FleetVmService>();
        var nginxProxySync = HttpContext.RequestServices.GetRequiredService<INginxProxySyncService>();

        var runtimeIds = await _context.TeamLabRuntimeShards
            .AsNoTracking()
            .Where(shard => shard.WorkerNodeId == nodeId && shard.Status != TeamLabRuntimeStatus.Destroyed)
            .Select(shard => shard.Runtime.PublicId)
            .Distinct()
            .ToArrayAsync(token);

        foreach (var runtimeId in runtimeIds)
        {
            try
            {
                var result = await runtimes.DestroyAsync(runtimeId, token);
                if (result.Status != TeamLabRuntimeStatus.Destroyed)
                    errors.Add($"TeamLab runtime {runtimeId:D} 清理失败：{result.Error}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Force deregister failed to cleanup TeamLab runtime {RuntimeId} on node {NodeId}.",
                    runtimeId, nodeId);
                errors.Add($"TeamLab runtime {runtimeId:D} 清理异常：{ex.Message}");
            }
        }

        var containers = await _context.Containers
            .Where(c => c.NodeId == nodeId && c.Status != ContainerStatus.Destroyed)
            .ToArrayAsync(token);

        foreach (var container in containers)
        {
            try
            {
                await containerManager.DestroyContainerAsync(container, token);
                if (container.Status == ContainerStatus.Destroyed)
                {
                    await _context.GameInstances
                        .Where(i => i.ContainerId == container.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.ContainerId, (Guid?)null), token);
                    await _context.ExerciseInstances
                        .Where(i => i.ContainerId == container.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.ContainerId, (Guid?)null), token);
                    await _context.AwdpServiceInstances
                        .Where(i => i.ContainerId == container.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.ContainerId, (Guid?)null), token);
                    await _context.GameChallenges
                        .Where(c => c.TestContainerId == container.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.TestContainerId, (Guid?)null), token);
                    _context.Containers.Remove(container);
                    await _context.SaveChangesAsync(token);
                    await nginxProxySync.TrySyncNowAsync("node force cleanup destroyed container", token);
                }
                else
                {
                    errors.Add($"容器 {container.ContainerId} 销毁未确认，当前状态：{container.Status}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Force deregister failed to destroy container {ContainerId}.",
                    container.ContainerId);
                errors.Add($"容器 {container.ContainerId} 销毁异常：{ex.Message}");
            }
        }

        var vms = await _context.VmInstances
            .Where(v => v.NodeId == nodeId &&
                        (v.Status == VmInstanceStatus.Creating || v.Status == VmInstanceStatus.Running))
            .ToArrayAsync(token);

        foreach (var vm in vms)
        {
            try
            {
                await fleetVm.DestroyVmAsync(vm, token);
                await _context.SaveChangesAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Force deregister failed to destroy VM {VmName}.", vm.VmName);
                errors.Add($"虚拟机 {vm.VmName} 销毁异常：{ex.Message}");
            }
        }

        var activeContainers = await _context.Containers.AsNoTracking()
            .CountAsync(c => c.NodeId == nodeId && c.Status != ContainerStatus.Destroyed, token);
        var activeVms = await _context.VmInstances.AsNoTracking()
            .CountAsync(v => v.NodeId == nodeId &&
                             (v.Status == VmInstanceStatus.Creating ||
                              v.Status == VmInstanceStatus.Running), token);
        var activeTeamLabRuntimes = await CountActiveTeamLabRuntimesAsync(nodeId, token);

        if (activeContainers > 0 || activeVms > 0 || activeTeamLabRuntimes > 0)
        {
            errors.Insert(0, "强制注销前仍有资源未确认清理，节点已暂停调度但不会注销。");
            return new(false, string.Join('\n', errors), activeContainers, activeVms, activeTeamLabRuntimes);
        }

        return new(true,
            errors.Count == 0 ? "节点资源已清理。" : $"节点资源已清理，期间有可忽略提示：{string.Join('\n', errors)}",
            activeContainers, activeVms, activeTeamLabRuntimes);
    }

    Task<int> CountActiveTeamLabRuntimesAsync(Guid nodeId, CancellationToken token) =>
        _context.TeamLabRuntimeShards.AsNoTracking()
            .Where(shard => shard.WorkerNodeId == nodeId &&
                            shard.Status != TeamLabRuntimeStatus.Stopped &&
                            shard.Status != TeamLabRuntimeStatus.Failed &&
                            shard.Status != TeamLabRuntimeStatus.Destroyed)
            .Select(shard => shard.RuntimeId)
            .Distinct()
            .CountAsync(token);

    [HttpPatch("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> UpdateNode(Guid id, [FromBody] UpdateNodeRequest request)
    {
        var token = HttpContext.RequestAborted;
        var node = await _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == id, token);
        if (node is null) return NotFound();

        if (request.IsSchedulable.HasValue)
            node.IsSchedulable = request.IsSchedulable.Value;

        if (request.MaxContainers.HasValue)
        {
            if (request.MaxContainers.Value < node.AllocatedContainers || request.MaxContainers.Value > 10000)
                return BadRequest(new { message = $"容器开启上限不能小于当前占用数 {node.AllocatedContainers}，且不能超过 10000。" });
            node.MaxContainers = request.MaxContainers.Value;
        }

        if (request.MaxVms.HasValue)
        {
            if (request.MaxVms.Value < node.AllocatedVms || request.MaxVms.Value > 1000)
                return BadRequest(new { message = $"虚拟机开启上限不能小于当前占用数 {node.AllocatedVms}，且不能超过 1000。" });
            node.MaxVms = request.MaxVms.Value;
        }

        if (request.IsStorageNode.HasValue || request.RegistryPort.HasValue)
            return BadRequest(new { message = "镜像仓库已固定为 10.24.0.28:5000，节点管理不再支持切换存储服务器。" });

        await _context.SaveChangesAsync(token);
        _logger.SystemLog(
            $"Worker node updated: node={node.Name}, id={node.Id}, schedulable={node.IsSchedulable}, maxContainers={node.MaxContainers}, maxVms={node.MaxVms}.",
            TaskStatus.Success, LogLevel.Information);

        return Ok(new
        {
            node.Id,
            node.IsSchedulable,
            node.IsLocal,
            node.MaxContainers,
            node.MaxVms
        });
    }

    [HttpPost("{id:guid}/teamlab/enable")]
    [RequireAdmin]
    public async Task<IActionResult> EnableTeamLabNetwork(Guid id, [FromBody] EnableTeamLabNetworkRequest request)
    {
        var token = HttpContext.RequestAborted;
        var node = await _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == id, token);
        if (node is null) return NotFound();

        var service = HttpContext.RequestServices.GetRequiredService<NodeTunnelService>();
        var result = request.DryRun
            ? await service.EnableDryRunAsync(node, token)
            : await service.MarkHealthyAsync(node, request.TunnelIp ?? string.Empty, token);

        _logger.SystemLog(
            $"Worker node TeamLab network {(request.DryRun ? "checked" : "enabled")}: node={node.Name}, id={node.Id}, success={result.Success}, message={result.Message}.",
            result.Success ? TaskStatus.Success : TaskStatus.Failed,
            result.Success ? LogLevel.Information : LogLevel.Warning);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/sync-agent")]
    [RequireAdmin]
    public async Task<IActionResult> SyncAgent(Guid id)
    {
        var token = HttpContext.RequestAborted;
        var node = await _context.WorkerNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, token);
        if (node is null) return NotFound();
        if (node.IsLocal)
            return BadRequest(new { message = "Local node is updated together with the platform deployment." });

        var requestBaseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var serverUrl = NodeDeployService.ResolveServerUrl(
            HttpContext.RequestServices.GetRequiredService<IConfiguration>(),
            requestBaseUrl);
        var agentClient = HttpContext.RequestServices.GetRequiredService<AgentClient>();

        try
        {
            var result = await agentClient.SyncAgentAsync(id,
                new AgentSyncRequest(
                    $"{serverUrl.TrimEnd('/')}/api/agent/download",
                    NodeDeployService.ComputeAgentBinarySha256()),
                token);
            _logger.SystemLog(
                $"Worker node Agent sync requested: node={node.Name}, id={node.Id}, success={result.Success}, message={result.Message}.",
                result.Success ? TaskStatus.Pending : TaskStatus.Failed,
                result.Success ? LogLevel.Information : LogLevel.Warning);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex) when (ex is AgentClientException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Agent sync failed on node {NodeId}", id);
            _logger.SystemLog(
                $"Worker node Agent sync failed: node={node.Name}, id={node.Id}, error={ex.Message}.",
                TaskStatus.Failed, LogLevel.Warning);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/heartbeat")]
    [AllowAnonymous]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    public async Task<IActionResult> Heartbeat(Guid id, [FromBody] HeartbeatRequest request)
    {
        if (request.CpuLoad < 0 || request.CpuLoad > 1
            || request.MemoryLoad < 0 || request.MemoryLoad > 1
            || request.CurrentContainers < 0 || request.CurrentVms < 0
            || request.UsedPorts < 0)
            return BadRequest(new { message = "Invalid metric values" });

        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();

        var authToken = HttpContext.Request.Headers["Authorization"]
            .ToString().Replace("Bearer ", "").Trim();
        if (!FixedTimeEquals(authToken, node.AuthToken))
            return Forbid();

        var runningTeamLabDockerAssets = await _context.TeamLabRuntimeAssets.CountAsync(
            a => (a.WorkerNodeId == node.Id ||
                  (a.WorkerNodeId == null && a.Shard != null && a.Shard.WorkerNodeId == node.Id))
                 && a.Runtime.Status == TeamLabRuntimeStatus.Running
                 && a.Kind == TeamLabResourceKind.Docker
                 && a.Status == TeamLabRuntimeStatus.Running,
            HttpContext.RequestAborted);
        var runningTeamLabVmAssets = await _context.TeamLabRuntimeAssets.CountAsync(
            a => (a.WorkerNodeId == node.Id ||
                  (a.WorkerNodeId == null && a.Shard != null && a.Shard.WorkerNodeId == node.Id))
                 && a.Runtime.Status == TeamLabRuntimeStatus.Running
                 && a.Kind == TeamLabResourceKind.Vm
                 && a.Status == TeamLabRuntimeStatus.Running,
            HttpContext.RequestAborted);

        node.CpuLoad = request.CpuLoad;
        node.MemoryLoad = request.MemoryLoad;
        node.CurrentContainers = request.CurrentContainers + runningTeamLabDockerAssets;
        node.CurrentVms = request.CurrentVms + runningTeamLabVmAssets;
        node.UsedPorts = request.UsedPorts;
        node.LastHeartbeat = DateTimeOffset.UtcNow;
        node.Status = NodeStatus.Online;
        if (!string.IsNullOrWhiteSpace(request.AgentVersion))
            node.TeamLabAgentVersion = request.AgentVersion.Trim();
        if (request.TeamLabProtocolVersion.HasValue)
            node.TeamLabProtocolVersion = Math.Max(0, request.TeamLabProtocolVersion.Value);
        if (!string.IsNullOrWhiteSpace(request.TeamLabFabricIp))
            node.TeamLabFabricIp = request.TeamLabFabricIp.Trim();
        if (request.TeamLabFabricStatus.HasValue)
            node.TeamLabFabricStatus = request.TeamLabFabricStatus.Value;
        if (request.TeamLabCapabilities is not null)
        {
            node.TeamLabCapabilitiesJson = JsonSerializer.Serialize(request.TeamLabCapabilities,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            node.Capabilities = WorkerNodeCapabilityHelper.FromTeamLabReport(
                request.TeamLabCapabilities.Docker,
                request.TeamLabCapabilities.Kvm,
                request.TeamLabCapabilities.KvmDevice,
                request.TeamLabCapabilities.CpuVirtualization);
        }
        await _context.SaveChangesAsync();

        var capacity = HttpContext.RequestServices.GetRequiredService<FleetCapacityReservationService>();
        await capacity.ReconcileReservedAsync(node.Id, HttpContext.RequestAborted);
        return Ok();
    }

    [HttpDelete("vms/{instanceId:guid}")]
    [RequireUser]
    public async Task<IActionResult> DestroyVm(Guid instanceId)
    {
        var vm = await _context.VmInstances.FindAsync(new object[] { instanceId }, HttpContext.RequestAborted);
        if (vm is null) return NotFound();

        var userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        if (vm.UserId.ToString() != userId)
            return Forbid();

        var fleetVm = HttpContext.RequestServices.GetRequiredService<FleetVmService>();
        await fleetVm.DestroyVmAsync(vm, HttpContext.RequestAborted);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("vms/{instanceId:guid}/admin")]
    [RequireAdmin]
    public async Task<IActionResult> DestroyVmAsAdmin(Guid instanceId)
    {
        var vm = await _context.VmInstances
            .FirstOrDefaultAsync(v => v.Id == instanceId, HttpContext.RequestAborted);
        if (vm is null) return NotFound();

        var fleetVm = HttpContext.RequestServices.GetRequiredService<FleetVmService>();
        await fleetVm.DestroyVmAsync(vm, HttpContext.RequestAborted);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("/api/agent/download")]
    [AllowAnonymous]
    public IActionResult DownloadAgent()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent", "gzctf-agent");
        if (!System.IO.File.Exists(path))
            return NotFound(new { message = "Agent binary not available" });
        return File(System.IO.File.OpenRead(path), "application/octet-stream", "gzctf-agent");
    }

    static string[] GetUnschedulableReasons(WorkerNode node)
    {
        var dockerReason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker);
        var kvmReason = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Kvm);

        if (dockerReason is null || kvmReason is null)
            return [];

        var reasons = new[]
        {
            dockerReason,
            kvmReason
        }.OfType<string>().Where(reason => !string.IsNullOrWhiteSpace(reason)).Distinct().ToArray();

        return reasons.Length == 2 && reasons[0] == reasons[1] ? [reasons[0]] : reasons;
    }

    static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    static object GetUnschedulableByCapability(WorkerNode node) => new
    {
        Docker = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker),
        Kvm = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Kvm),
        TeamLabNetwork = WeightedScheduler.GetTeamLabFabricUnschedulableReason(node),
        TeamLabDocker = WeightedScheduler.GetTeamLabAssetHostUnschedulableReason(node, requiresDocker: true, requiresVm: false),
        TeamLabVm = WeightedScheduler.GetTeamLabAssetHostUnschedulableReason(node, requiresDocker: false, requiresVm: true)
    };

    static string[] GetSchedulableCapabilities(WorkerNode node) =>
    [
        .. new[]
        {
            WeightedScheduler.CanHost(node, NodeCapability.Docker) ? nameof(NodeCapability.Docker) : null,
            WeightedScheduler.CanHost(node, NodeCapability.Kvm) ? nameof(NodeCapability.Kvm) : null,
            WeightedScheduler.CanHostTeamLabFabric(node) ? "TeamLabNetwork" : null,
            WeightedScheduler.CanHostTeamLabDocker(node) ? "TeamLabDocker" : null,
            WeightedScheduler.CanHostTeamLabVm(node) ? "TeamLabVm" : null
        }.OfType<string>()
    ];

    PublicPortPool GetPublicPortPool()
        => CreatePortPool(
            _portAllocator.CurrentRange.Start,
            _portAllocator.CurrentRange.End,
            _portAllocator.CurrentRange.Mode,
            _portAllocator.CurrentRange.Mode);

    internal static PublicPortPool ResolvePublicPortPool(NginxProxyConfig? nginx, DockerConfig? docker)
    {
        if (nginx?.Enable == true)
            return CreatePortPool(nginx.ListenPortStart, nginx.ListenPortEnd, "nginx", "nginx-unconfigured");

        return CreatePortPool(docker?.PublicPortStart, docker?.PublicPortEnd, "docker", "docker-random");
    }

    internal static PublicPortPool CreatePortPool(int? start, int? end, string mode, string emptyMode)
    {
        if (start is null || end is null || start <= 0 || end < start || end > ushort.MaxValue)
            return new PublicPortPool(null, null, 0, emptyMode);

        return new PublicPortPool(start.Value, end.Value, end.Value - start.Value + 1, mode);
    }

    async Task<int> GetPublicPortUsageAsync(PublicPortPool portPool, CancellationToken token)
    {
        if (portPool.Start is null || portPool.End is null)
            return 0;

        var query = _context.Containers.AsNoTracking()
            .Where(c => c.PublicPort.HasValue
                && c.PublicPort.Value >= portPool.Start.Value
                && c.PublicPort.Value <= portPool.End.Value);

        if (portPool.Mode is "nginx")
        {
            var publicEntry = _containerProvider.PublicEntry;
            if (string.IsNullOrWhiteSpace(publicEntry))
                return 0;

            query = query.Where(c => c.Status == ContainerStatus.Running
                && c.PublicIP == publicEntry
                && c.NodeId != null
                && c.Node != null
                && !c.IsProxy);
        }
        else
        {
            query = query.Where(c => c.Status != ContainerStatus.Destroyed);
        }

        return await query.Select(c => c.PublicPort!.Value)
            .Distinct()
            .CountAsync(token);
    }

    static NodeResourceItemModel ToNodeContainerResource(Container container)
    {
        var instance = container.GameInstance;
        var challenge = instance?.Challenge;
        var participation = instance?.Participation;
        var team = participation?.Team;
        var status = container.Status.ToString();
        var active = container.Status is ContainerStatus.Pending or ContainerStatus.Running;

        return new NodeResourceItemModel
        {
            Kind = "container",
            Id = container.Id.ToString(),
            Name = challenge?.Title ?? container.Image.Split('/').LastOrDefault() ?? "Container",
            Status = status,
            IsActive = active,
            StartedAt = container.StartedAt,
            ExpectedStopAt = active ? container.ExpectStopAt : null,
            StoppedAt = active ? null : container.ExpectStopAt,
            Duration = FormatDuration(container.StartedAt, active ? DateTimeOffset.UtcNow : container.ExpectStopAt),
            Image = container.Image,
            RuntimeId = ShortenRuntimeId(container.ContainerId),
            Entry = container.Entry,
            Ip = container.PublicIP ?? container.IP,
            Port = container.PublicPort ?? container.Port,
            GameId = challenge?.GameId,
            GameTitle = challenge?.Game.Title,
            ChallengeId = challenge?.Id,
            ChallengeTitle = challenge?.Title,
            ChallengeCategory = challenge?.Category.ToString(),
            TeamId = team?.Id,
            TeamName = team?.Name
        };
    }

    static async Task<string?> ResolveVmEntryAsync(VmInstance vm, GuacamoleService? guacService, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(vm.GuacamoleConnectionId) && guacService is not null)
        {
            var authUrl = await guacService.GetAuthenticatedConnectionUrlAsync(vm.GuacamoleConnectionId, token);
            if (!string.IsNullOrWhiteSpace(authUrl))
                return authUrl;
        }

        return vm.RdpUrl;
    }

    internal static NodeResourceItemModel ToNodeVmResource(VmInstance vm,
        IReadOnlyDictionary<Guid, string?> users,
        IReadOnlyDictionary<(Guid UserId, int GameId), Team> teamsByUser,
        string? entry)
    {
        var challenge = vm.Challenge;
        var ownerTeam = challenge is null
            ? null
            : teamsByUser.GetValueOrDefault((vm.UserId, challenge.GameId));
        users.TryGetValue(vm.UserId, out var userName);
        var active = vm.Status is VmInstanceStatus.Creating or VmInstanceStatus.Running;

        return new NodeResourceItemModel
        {
            Kind = "vm",
            Id = vm.Id.ToString(),
            Name = vm.VmName,
            Status = vm.Status.ToString(),
            IsActive = active,
            StartedAt = vm.CreatedAt,
            ExpectedStopAt = null,
            StoppedAt = vm.DestroyedAt,
            Duration = FormatDuration(vm.CreatedAt, vm.DestroyedAt ?? DateTimeOffset.UtcNow),
            RuntimeId = vm.VmName,
            Entry = entry,
            Ip = vm.IpAddress,
            GameId = challenge?.GameId,
            GameTitle = challenge?.Game.Title,
            ChallengeId = challenge?.Id,
            ChallengeTitle = challenge?.Title,
            ChallengeCategory = challenge?.Category.ToString(),
            TeamId = ownerTeam?.Id,
            TeamName = ownerTeam?.Name,
            UserId = vm.UserId,
            UserName = userName,
            ProviderName = vm.ProviderName,
            OsType = vm.OSType.ToString()
        };
    }

    static NodeResourceItemModel ToNodeTeamLabResource(
        TeamLabRuntimeAsset asset,
        NodeTeamLabBindingView? binding)
    {
        var runtime = asset.Runtime;
        var active = asset.Status is TeamLabRuntimeStatus.Pending
            or TeamLabRuntimeStatus.Planning
            or TeamLabRuntimeStatus.Scheduled
            or TeamLabRuntimeStatus.Deploying
            or TeamLabRuntimeStatus.Probing
            or TeamLabRuntimeStatus.Running
            or TeamLabRuntimeStatus.CleanupPending
            or TeamLabRuntimeStatus.Destroying;
        var startedAt = runtime.CreatedAt;
        var stoppedAt = active ? null : runtime.UpdatedAt;
        var provider = asset.Shard is null
            ? asset.Kind.ToString()
            : $"{asset.Kind} / shard {asset.Shard.Id}";

        return new NodeResourceItemModel
        {
            Kind = binding is null ? "teamlab" : "pentest",
            Id = asset.Id.ToString(),
            Name = string.IsNullOrWhiteSpace(asset.Name) ? asset.TopologyKey : asset.Name,
            Status = asset.Status.ToString(),
            IsActive = active,
            StartedAt = startedAt,
            ExpectedStopAt = null,
            StoppedAt = stoppedAt,
            Duration = FormatDuration(startedAt, stoppedAt ?? DateTimeOffset.UtcNow),
            Image = asset.Image,
            RuntimeId = runtime.PublicId.ToString("D"),
            Entry = asset.IpAddress,
            Ip = asset.IpAddress,
            Port = null,
            GameId = binding?.GameId,
            GameTitle = binding?.GameTitle,
            ChallengeId = null,
            ChallengeTitle = asset.Name,
            ChallengeCategory = "TeamLab",
            TeamId = binding?.TeamId,
            TeamName = binding?.TeamName,
            ProviderName = provider,
            OsType = asset.Kind.ToString()
        };
    }

    static string ShortenRuntimeId(string id) =>
        string.IsNullOrWhiteSpace(id) || id.Length <= 12 ? id : id[..12];

    static string FormatDuration(DateTimeOffset start, DateTimeOffset end)
    {
        var span = end - start;
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}天 {span.Hours}小时";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}小时 {span.Minutes}分钟";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes}分钟";
        return $"{Math.Max(0, (int)span.TotalSeconds)}秒";
    }
}

[ApiController]
[Route("api/v1/deployment-targets")]
[Produces(MediaTypeNames.Application.Json)]
public class DeploymentTargetsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly DeploymentQueueService _queue;
    private readonly DeploymentQueueViewService _queueView;
    private readonly ILogger<DeploymentTargetsController> _logger;

    public DeploymentTargetsController(AppDbContext context, DeploymentQueueService queue,
        DeploymentQueueViewService queueView,
        ILogger<DeploymentTargetsController> logger)
    {
        _context = context;
        _queue = queue;
        _queueView = queueView;
        _logger = logger;
    }

    [HttpGet]
    [RequireAdmin]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(await _queueView.ListAsync(status, page, pageSize, HttpContext.RequestAborted));
    }

    [HttpGet("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> GetById(Guid id)
    {
        var target = await _context.DeploymentTargets
            .Include(t => t.TargetNode)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (target is null) return NotFound();
        return Ok(new
        {
            target.Id, target.TargetNodeId, target.Type, target.Action, target.Status,
            TargetNodeName = target.TargetNode == null ? null : target.TargetNode.Name,
            TargetNodeHost = target.TargetNode == null ? null : target.TargetNode.HostAddress,
            target.ResultPort, target.ResultHost,
            target.CreatedAt, target.CompletedAt, target.ErrorMessage
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var token = HttpContext?.RequestAborted ?? CancellationToken.None;
        var ticket = await _context.DeploymentQueueTickets
            .Include(t => t.TargetNode)
            .Include(t => t.DeploymentTarget).ThenInclude(t => t!.TargetNode)
            .SingleOrDefaultAsync(t => t.Id == id, token);
        if (ticket is not null)
        {
            await _queue.CancelAsync(ticket.Id, "Deployment queue ticket was cancelled by administrator.", token);
            var node = ticket.TargetNode ?? ticket.DeploymentTarget?.TargetNode;
            _logger.SystemLog(
                $"Deployment queue ticket {ticket.Id} cancelled by administrator: kind={ticket.Kind}, game={ticket.GameId}, team={ticket.OwnerTeamId}, user={ticket.OwnerUserId}, challenge={ticket.ChallengeId}, node={node?.Name ?? node?.HostAddress ?? "unassigned"}.",
                TaskStatus.Exit, LogLevel.Information);
            return NoContent();
        }

        var target = await _context.DeploymentTargets
            .Include(t => t.TargetNode)
            .SingleOrDefaultAsync(t => t.Id == id, token);
        if (target is null) return NotFound();
        if (target.Status == TargetStatus.Pending ||
            target.Status == TargetStatus.Assigned ||
            target.Status == TargetStatus.Creating ||
            target.Status == TargetStatus.Running)
        {
            var activeTicket = await _context.DeploymentQueueTickets
                .AsNoTracking()
                .Where(t => t.DeploymentTargetId == target.Id &&
                            (t.Status == DeploymentQueueTicketStatus.Pending ||
                             t.Status == DeploymentQueueTicketStatus.Assigned ||
                             t.Status == DeploymentQueueTicketStatus.Creating))
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
            if (activeTicket is not null)
            {
                await _queue.CancelAsync(activeTicket.Id, "Deployment target was cancelled by administrator.",
                    token);
                _logger.SystemLogDeploymentTarget("cancelled", target, target.TargetNode);
                return NoContent();
            }

            target.Status = TargetStatus.Cancelled;
            target.CompletedAt = DateTimeOffset.UtcNow;
            target.ErrorMessage = "Deployment target was cancelled by administrator.";
            await _context.SaveChangesAsync();
            _logger.SystemLogDeploymentTarget("cancelled", target, target.TargetNode);
        }
        return NoContent();
    }
}

public class NodeDeployRequest
{
    [Required] public string HostAddress { get; set; } = string.Empty;
    [Required] public string Username { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
    public string? NodeName { get; set; }
}

public class UpdateNodeRequest
{
    public bool? IsSchedulable { get; set; }
    public int? MaxContainers { get; set; }
    public int? MaxVms { get; set; }
    public bool? IsStorageNode { get; set; }
    public int? RegistryPort { get; set; }
}

public class EnableTeamLabNetworkRequest
{
    public bool DryRun { get; set; } = true;
    public string? TunnelIp { get; set; }
}

public class HeartbeatRequest
{
    public float CpuLoad { get; set; }
    public float MemoryLoad { get; set; }
    public int CurrentContainers { get; set; }
    public int CurrentVms { get; set; }
    public int UsedPorts { get; set; }
    public string? AgentVersion { get; set; }
    public int? TeamLabProtocolVersion { get; set; }
    public string? TeamLabFabricIp { get; set; }
    public TeamLabFabricStatus? TeamLabFabricStatus { get; set; }
    public TeamLabNodeCapabilityReport? TeamLabCapabilities { get; set; }
}

public class TeamLabNodeCapabilityReport
{
    public bool Docker { get; set; }
    public bool Kvm { get; set; }
    public bool KvmDevice { get; set; }
    public bool CpuVirtualization { get; set; }
    public bool WireGuard { get; set; }
    public bool Iptables { get; set; }
    public bool Nftables { get; set; }
    public bool Tcpdump { get; set; }
    public bool Dumpcap { get; set; }
}

record NodeForceCleanupResult(bool Success, string Message, int ActiveContainers, int ActiveVms,
    int ActiveTeamLabRuntimes);

record NodeTeamLabBindingView(int RuntimeId, int GameId, string GameTitle, int TeamId, string TeamName);

record PublicPortPool(int? Start, int? End, int Total, string Mode);
