using System.Collections.Concurrent;
using GZCTF.Modules.Identity.Contracts;

namespace GZCTF.Hubs;

/// <summary>Tracks server-side abort callbacks, without retaining identity credentials.</summary>
public sealed class UserHubAccountConnections : IUserAccountRevocationParticipant
{
    private readonly ConcurrentDictionary<string, (Guid UserId, Action Abort)> connections = new();
    public Guid[] UserIds => connections.Values.Select(c => c.UserId).Distinct().ToArray();
    public void Register(Guid userId, string connectionId, Action abort) => connections[connectionId] = (userId, abort);
    public void Remove(string connectionId) => connections.TryRemove(connectionId, out _);
    public void Abort(Guid userId)
    {
        foreach (var entry in connections.Where(c => c.Value.UserId == userId))
            if (connections.TryRemove(entry.Key, out var connection))
                connection.Abort();
    }
    public Task PrepareAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task CompleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        Abort(userId);
        return Task.CompletedTask;
    }
}
