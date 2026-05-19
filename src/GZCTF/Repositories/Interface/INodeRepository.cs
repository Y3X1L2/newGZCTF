using GZCTF.Models.Data;

namespace GZCTF.Repositories.Interface;

public interface INodeRepository
{
    Task<List<WorkerNode>> GetOnlineNodesAsync(CancellationToken token);
    Task<List<WorkerNode>> GetAllNodesAsync(CancellationToken token);
    Task<WorkerNode?> GetNodeByIdAsync(Guid id, CancellationToken token);
    Task<int> MarkStaleNodesOfflineAsync(TimeSpan timeout, CancellationToken token);
}
