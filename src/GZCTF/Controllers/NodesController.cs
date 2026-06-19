using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Admin;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/v1/nodes")]
[Produces(MediaTypeNames.Application.Json)]
public class NodesController : ControllerBase
{
    private readonly INodeRepository _nodeRepo;
    private readonly AppDbContext _context;
    private readonly ILogger<NodesController> _logger;

    public NodesController(INodeRepository nodeRepo, AppDbContext context, ILogger<NodesController> logger)
    { _nodeRepo = nodeRepo; _context = context; _logger = logger; }

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
            return BadRequest(new { message = result.Message });

        return Ok(result);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List()
    {
        var nodes = await _nodeRepo.GetAllNodesAsync(HttpContext.RequestAborted);
        var now = DateTimeOffset.UtcNow;
        return Ok(nodes.Select(n => new
        {
            n.Id, n.Name, n.HostAddress, Status = n.GetEffectiveStatus(now), n.Capabilities,
            n.CpuLoad, n.MemoryLoad, n.CurrentContainers, n.MaxContainers,
            n.CurrentVms, n.MaxVms, n.UsedPorts, n.TotalPorts, n.LastHeartbeat,
            n.IsSchedulable, n.IsLocal, n.AgentPort,
            UnschedulableReasons = GetUnschedulableReasons(n),
            UnschedulableByCapability = GetUnschedulableByCapability(n),
            SchedulableCapabilities = GetSchedulableCapabilities(n)
        }));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Detail(Guid id)
    {
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();
        var now = DateTimeOffset.UtcNow;
        return Ok(new
        {
            node.Id, node.Name, node.HostAddress, Status = node.GetEffectiveStatus(now), node.Capabilities,
            node.CpuLoad, node.MemoryLoad, node.CurrentContainers, node.MaxContainers,
            node.CurrentVms, node.MaxVms, node.UsedPorts, node.TotalPorts, node.LastHeartbeat,
            node.IsSchedulable, node.IsLocal, node.AgentPort,
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
                .OrderByDescending(m => m.GameId)
                .ToListAsync(token);
            var teamsByUser = teamRows
                .GroupBy(m => m.UserId)
                .ToDictionary(g => g.Key, g => g.First().Team);

            resources.AddRange(vms.Select(vm => ToNodeVmResource(vm, users, teamsByUser)));
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
            Items = items
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> Deregister(Guid id, [FromQuery] bool force = false)
    {
        var token = HttpContext.RequestAborted;
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
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
                    cleanup.ActivePentestEnvironments
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
        var activePentestEnvironments = await _context.PenetrationTeamEnvironments.AsNoTracking()
            .CountAsync(e => e.NodeId == id &&
                             e.Status != PenetrationRuntimeStatus.Stopped &&
                             e.Status != PenetrationRuntimeStatus.Failed, token);

        if (activeContainers > 0 || activeVms > 0 || activePentestEnvironments > 0)
            return BadRequest(new
            {
                message = "该节点仍承载运行资源，请先停止或清理后再注销。",
                activeContainers,
                activeVms,
                activePentestEnvironments
            });

        await using var transaction = await _context.Database.BeginTransactionAsync(token);

        var now = DateTimeOffset.UtcNow;
        await _context.DeploymentTargets
            .Where(t => t.TargetNodeId == id
                        && (t.Status == TargetStatus.Pending ||
                            t.Status == TargetStatus.Assigned ||
                            t.Status == TargetStatus.Creating ||
                            t.Status == TargetStatus.Running))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(t => t.Status, TargetStatus.Cancelled)
                .SetProperty(t => t.CompletedAt, (DateTimeOffset?)now)
                .SetProperty(t => t.ErrorMessage, "Target node was deregistered."), token);

        await _context.DeploymentTargets
            .Where(t => t.TargetNodeId == id)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(t => t.TargetNodeId, (Guid?)null), token);

        await _context.Containers
            .Where(c => c.NodeId == id)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(c => c.NodeId, (Guid?)null), token);

        await _context.VmInstances
            .Where(v => v.NodeId == id)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(v => v.NodeId, (Guid?)null), token);

        await _context.PenetrationTeamEnvironments
            .Where(e => e.NodeId == id)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(e => e.NodeId, (Guid?)null), token);

        _context.WorkerNodes.Remove(node);
        await _context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);

        return NoContent();
    }

    async Task<NodeForceCleanupResult> ForceCleanupNodeResources(Guid nodeId, CancellationToken token)
    {
        var errors = new List<string>();
        var pentest = HttpContext.RequestServices.GetRequiredService<PenetrationService>();
        var containerManager = HttpContext.RequestServices.GetRequiredService<IContainerManager>();
        var fleetVm = HttpContext.RequestServices.GetRequiredService<FleetVmService>();

        var environments = await _context.PenetrationTeamEnvironments
            .AsNoTracking()
            .Where(e => e.NodeId == nodeId &&
                        e.Status != PenetrationRuntimeStatus.Stopped &&
                        e.Status != PenetrationRuntimeStatus.Failed)
            .Select(e => new { e.GameId, e.TeamId })
            .ToArrayAsync(token);

        foreach (var environment in environments)
        {
            try
            {
                var result = await pentest.CleanupTeamEnvironment(environment.GameId, environment.TeamId, token);
                if (!result.Success)
                    errors.Add($"渗透环境 {environment.GameId}/{environment.TeamId} 清理失败：{result.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Force deregister failed to cleanup penetration environment on node {NodeId}, game {GameId}, team {TeamId}.",
                    nodeId, environment.GameId, environment.TeamId);
                errors.Add($"渗透环境 {environment.GameId}/{environment.TeamId} 清理异常：{ex.Message}");
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
                    await _context.PenetrationRuntimeNodes
                        .Where(r => r.ContainerId == container.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(r => r.ContainerId, (Guid?)null)
                            .SetProperty(r => r.Status, PenetrationRuntimeStatus.Stopped), token);
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
        var activePentestEnvironments = await _context.PenetrationTeamEnvironments.AsNoTracking()
            .CountAsync(e => e.NodeId == nodeId &&
                             e.Status != PenetrationRuntimeStatus.Stopped &&
                             e.Status != PenetrationRuntimeStatus.Failed, token);

        if (activeContainers > 0 || activeVms > 0 || activePentestEnvironments > 0)
        {
            errors.Insert(0, "强制注销前仍有资源未确认清理，节点已暂停调度但不会注销。");
            return new(false, string.Join('\n', errors), activeContainers, activeVms, activePentestEnvironments);
        }

        return new(true,
            errors.Count == 0 ? "节点资源已清理。" : $"节点资源已清理，期间有可忽略提示：{string.Join('\n', errors)}",
            activeContainers, activeVms, activePentestEnvironments);
    }

    [HttpPatch("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> UpdateNode(Guid id, [FromBody] UpdateNodeRequest request)
    {
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();

        if (request.IsSchedulable.HasValue)
            node.IsSchedulable = request.IsSchedulable.Value;

        await _context.SaveChangesAsync();
        return Ok(new { node.Id, node.IsSchedulable });
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
        if (string.IsNullOrEmpty(authToken) || authToken != node.AuthToken)
            return Forbid();

        node.CpuLoad = request.CpuLoad;
        node.MemoryLoad = request.MemoryLoad;
        node.CurrentContainers = request.CurrentContainers;
        node.CurrentVms = request.CurrentVms;
        node.UsedPorts = request.UsedPorts;
        node.LastHeartbeat = DateTimeOffset.UtcNow;
        node.Status = NodeStatus.Online;
        await _context.SaveChangesAsync();
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

    static object GetUnschedulableByCapability(WorkerNode node) => new
    {
        Docker = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Docker),
        Kvm = WeightedScheduler.GetUnschedulableReason(node, NodeCapability.Kvm)
    };

    static string[] GetSchedulableCapabilities(WorkerNode node) =>
    [
        .. new[]
        {
            WeightedScheduler.CanHost(node, NodeCapability.Docker) ? nameof(NodeCapability.Docker) : null,
            WeightedScheduler.CanHost(node, NodeCapability.Kvm) ? nameof(NodeCapability.Kvm) : null
        }.OfType<string>()
    ];

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
            Id = container.Id,
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

    static NodeResourceItemModel ToNodeVmResource(VmInstance vm,
        IReadOnlyDictionary<Guid, string?> users,
        IReadOnlyDictionary<Guid, Team> teamsByUser)
    {
        var challenge = vm.Challenge;
        teamsByUser.TryGetValue(vm.UserId, out var ownerTeam);
        users.TryGetValue(vm.UserId, out var userName);
        var active = vm.Status is VmInstanceStatus.Creating or VmInstanceStatus.Running;

        return new NodeResourceItemModel
        {
            Kind = "vm",
            Id = vm.Id,
            Name = vm.VmName,
            Status = vm.Status.ToString(),
            IsActive = active,
            StartedAt = vm.CreatedAt,
            ExpectedStopAt = null,
            StoppedAt = vm.DestroyedAt,
            Duration = FormatDuration(vm.CreatedAt, vm.DestroyedAt ?? DateTimeOffset.UtcNow),
            RuntimeId = vm.VmName,
            Entry = vm.RdpUrl,
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

    public DeploymentTargetsController(AppDbContext context)
    { _context = context; }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List(
        [FromQuery] TargetStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.DeploymentTargets.AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id, t.TargetNodeId, t.Type, t.Action, t.Status,
                TargetNodeName = t.TargetNode == null ? null : t.TargetNode.Name,
                TargetNodeHost = t.TargetNode == null ? null : t.TargetNode.HostAddress,
                t.Payload, t.ResultPort, t.ResultHost,
                t.CreatedAt, t.CompletedAt, t.ErrorMessage
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id)
    {
        var target = await _context.DeploymentTargets.FindAsync(id);
        if (target is null) return NotFound();
        return Ok(new
        {
            target.Id, target.TargetNodeId, target.Type, target.Action, target.Status,
            target.Payload, target.ResultPort, target.ResultHost,
            target.CreatedAt, target.CompletedAt, target.ErrorMessage
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var target = await _context.DeploymentTargets.FindAsync(id);
        if (target is null) return NotFound();
        if (target.Status == TargetStatus.Pending ||
            target.Status == TargetStatus.Assigned ||
            target.Status == TargetStatus.Creating ||
            target.Status == TargetStatus.Running)
        {
            target.Status = TargetStatus.Cancelled;
            target.CompletedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();
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
}

public class HeartbeatRequest
{
    public float CpuLoad { get; set; }
    public float MemoryLoad { get; set; }
    public int CurrentContainers { get; set; }
    public int CurrentVms { get; set; }
    public int UsedPorts { get; set; }
}

record NodeForceCleanupResult(bool Success, string Message, int ActiveContainers, int ActiveVms,
    int ActivePentestEnvironments);
