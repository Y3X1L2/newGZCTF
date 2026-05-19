using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

/// <summary>
/// Worker node management and fleet operations API.
/// </summary>
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

    /// <summary>Register a new worker node.</summary>
    [HttpPost]
    [RequireAdmin]
    public async Task<IActionResult> Register([FromBody] NodeRegisterRequest request)
    {
        var node = new WorkerNode
        {
            Name = request.Name, HostAddress = request.HostAddress,
            AuthToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            Capabilities = request.Capabilities, Status = NodeStatus.Online,
            MaxContainers = request.MaxContainers, MaxVms = request.MaxVms
        };
        _context.WorkerNodes.Add(node);
        await _context.SaveChangesAsync();
        return Ok(new { node.Id, node.AuthToken });
    }

    /// <summary>List all nodes with current status.</summary>
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

    /// <summary>Get node detail.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Detail(Guid id)
    {
        var node = await _nodeRepo.GetNodeByIdAsync(id, HttpContext.RequestAborted);
        if (node is null) return NotFound();
        return Ok(node);
    }

    /// <summary>Deregister a node.</summary>
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

    /// <summary>Agent heartbeat with load metrics.</summary>
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
