using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Fleet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        return Ok(nodes.Select(n => new
        {
            n.Id, n.Name, n.HostAddress, n.Status, n.Capabilities,
            n.CpuLoad, n.MemoryLoad, n.CurrentContainers, n.MaxContainers,
            n.CurrentVms, n.MaxVms, n.LastHeartbeat
        }));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Detail(Guid id)
    {
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();
        return Ok(new
        {
            node.Id, node.Name, node.HostAddress, node.Status, node.Capabilities,
            node.CpuLoad, node.MemoryLoad, node.CurrentContainers, node.MaxContainers,
            node.CurrentVms, node.MaxVms, node.UsedPorts, node.TotalPorts, node.LastHeartbeat
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> Deregister(Guid id)
    {
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();
        node.Status = NodeStatus.Offline;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/heartbeat")]
    [Authorize]
    public async Task<IActionResult> Heartbeat(Guid id, [FromBody] HeartbeatRequest request)
    {
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();
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

public class NodeRegisterRequest
{
    [Required, MaxLength(128)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(256)] public string HostAddress { get; set; } = string.Empty;
    public NodeCapability Capabilities { get; set; } = NodeCapability.Docker;
    public int MaxContainers { get; set; } = 20;
    public int MaxVms { get; set; } = 5;
}

public class HeartbeatRequest
{
    public float CpuLoad { get; set; }
    public float MemoryLoad { get; set; }
    public int CurrentContainers { get; set; }
    public int CurrentVms { get; set; }
    public int UsedPorts { get; set; }
}
