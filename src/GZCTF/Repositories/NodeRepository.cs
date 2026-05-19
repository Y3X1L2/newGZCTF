using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class NodeRepository : INodeRepository
{
    private readonly AppDbContext _context;

    public NodeRepository(AppDbContext context) => _context = context;

    public Task<List<WorkerNode>> GetOnlineNodesAsync(CancellationToken token) =>
        _context.WorkerNodes.Where(n => n.Status == NodeStatus.Online).ToListAsync(token);

    public Task<List<WorkerNode>> GetAllNodesAsync(CancellationToken token) =>
        _context.WorkerNodes.ToListAsync(token);

    public Task<WorkerNode?> GetNodeByIdAsync(Guid id, CancellationToken token) =>
        _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == id, token);

    public async Task<int> MarkStaleNodesOfflineAsync(TimeSpan timeout, CancellationToken token)
    {
        var cutoff = DateTimeOffset.UtcNow - timeout;
        var stale = await _context.WorkerNodes
            .Where(n => n.Status == NodeStatus.Online && n.LastHeartbeat < cutoff)
            .ToListAsync(token);
        foreach (var node in stale) node.Status = NodeStatus.Offline;
        if (stale.Count > 0) await _context.SaveChangesAsync(token);
        return stale.Count;
    }
}
