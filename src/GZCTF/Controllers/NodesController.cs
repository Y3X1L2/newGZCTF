using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
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
        var result = await deployer.DeployToServerAsync(
            request.HostAddress, request.Username, request.Password,
            request.NodeName, HttpContext.RequestAborted);

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
            n.CurrentVms, n.MaxVms, n.LastHeartbeat,
            n.IsSchedulable, n.IsLocal, n.AgentPort
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
            node.IsSchedulable, node.IsLocal, node.AgentPort
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> Deregister(Guid id)
    {
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();
        if (node.IsLocal) return BadRequest(new { message = "Cannot deregister local node" });
        _context.WorkerNodes.Remove(node);
        await _context.SaveChangesAsync();
        return NoContent();
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

    [HttpGet("/api/agent/download")]
    [AllowAnonymous]
    public IActionResult DownloadAgent()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent", "gzctf-agent");
        if (!System.IO.File.Exists(path))
            return NotFound(new { message = "Agent binary not available" });
        return File(System.IO.File.OpenRead(path), "application/octet-stream", "gzctf-agent");
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
        if (target.Status == TargetStatus.Pending || target.Status == TargetStatus.Running)
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
