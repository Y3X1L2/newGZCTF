using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public static class TeamLabCapacityFacts
{
    public static async Task<TeamLabShardSlotCount[]> LoadAsync(
        AppDbContext context,
        int runtimeId,
        CancellationToken cancellationToken)
    {
        var facts = await LoadManyAsync(context, [runtimeId], cancellationToken);
        return facts.GetValueOrDefault(runtimeId) ?? [];
    }

    public static async Task<IReadOnlyDictionary<int, TeamLabShardSlotCount[]>> LoadManyAsync(
        AppDbContext context,
        IReadOnlyCollection<int> runtimeIds,
        CancellationToken cancellationToken)
    {
        if (runtimeIds.Count == 0) return new Dictionary<int, TeamLabShardSlotCount[]>();
        var ids = runtimeIds.Distinct().ToArray();
        var generations = await context.TeamLabRuntimes.AsNoTracking()
            .Where(runtime => ids.Contains(runtime.Id))
            .ToDictionaryAsync(runtime => runtime.Id, runtime => runtime.Generation, cancellationToken);
        var shards = await context.TeamLabRuntimeShards.AsNoTracking()
            .Include(shard => shard.Assets)
            .Where(shard => ids.Contains(shard.RuntimeId))
            .ToArrayAsync(cancellationToken);

        return generations.ToDictionary(
            runtime => runtime.Key,
            runtime => shards
                .Where(shard => shard.RuntimeId == runtime.Key && shard.Generation == runtime.Value)
                .Select(shard => new TeamLabShardSlotCount(
                    shard.WorkerNodeId,
                    shard.Assets.Count(asset =>
                        asset.Generation == runtime.Value && asset.Kind == TeamLabResourceKind.Docker),
                    shard.Assets.Count(asset =>
                        asset.Generation == runtime.Value && asset.Kind == TeamLabResourceKind.Vm)))
                .Where(item => item.DockerSlots > 0 || item.VmSlots > 0)
                .OrderBy(item => item.WorkerNodeId)
                .ToArray());
    }
}
