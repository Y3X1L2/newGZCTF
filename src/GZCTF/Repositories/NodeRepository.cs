using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class NodeRepository : INodeRepository
{
    private readonly AppDbContext _context;
    private readonly IOperationalEventWriter _events;

    public NodeRepository(AppDbContext context, IOperationalEventWriter events)
    {
        _context = context;
        _events = events;
    }

    public Task<List<WorkerNode>> GetOnlineNodesAsync(CancellationToken token)
    {
        var cutoff = DateTimeOffset.UtcNow - WorkerNode.DefaultHeartbeatTimeout;

        return _context.WorkerNodes
            .Where(n => n.Status == NodeStatus.Online
                && (n.IsLocal || (n.LastHeartbeat.HasValue && n.LastHeartbeat >= cutoff)))
            .ToListAsync(token);
    }

    public Task<List<WorkerNode>> GetAllNodesAsync(CancellationToken token) =>
        _context.WorkerNodes.ToListAsync(token);

    public Task<WorkerNode?> GetNodeByIdAsync(Guid id, CancellationToken token) =>
        _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == id, token);

    public async Task<int> MarkStaleNodesOfflineAsync(TimeSpan timeout, CancellationToken token)
    {
        var cutoff = DateTimeOffset.UtcNow - timeout;
        var stale = await _context.WorkerNodes
            .Where(n => n.Status == NodeStatus.Online
                && !n.IsLocal
                && (!n.LastHeartbeat.HasValue || n.LastHeartbeat < cutoff))
            .ToListAsync(token);
        foreach (var node in stale)
        {
            node.Status = NodeStatus.Offline;
            _events.Append(NodeOperationalEvents.Create(
                node,
                OperationalEventCodes.Node.Offline,
                OperationalEventOutcome.Observed,
                "Worker node heartbeat expired and the node became offline.",
                OperationalEventSeverity.Warning,
                detail: new Dictionary<string, object?>
                {
                    ["previousStatus"] = NodeStatus.Online.ToString(),
                    ["currentStatus"] = NodeStatus.Offline.ToString(),
                    ["reasonCode"] = "heartbeat_timeout"
                }));
        }
        if (stale.Count > 0) await _context.SaveChangesAsync(token);
        return stale.Count;
    }
}
