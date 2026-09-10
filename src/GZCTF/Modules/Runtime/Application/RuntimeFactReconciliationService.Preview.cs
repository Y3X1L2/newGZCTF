using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Application;

public sealed partial class RuntimeFactReconciliationService
{
    // Read-only: no capacity renewal, corrections, events or ticket recovery during a preview.
    public async Task<RuntimeDifferencePreview> PreviewTeamLabAsync(int runtimeId, CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking().SingleAsync(item => item.Id == runtimeId, token);
        var assets = await context.TeamLabRuntimeAssets.AsNoTracking()
            .Where(item => item.RuntimeId == runtimeId && item.Generation == runtime.Generation)
            .OrderBy(item => item.Id).ToArrayAsync(token);
        var nodeIds = await context.TeamLabRuntimeShards.AsNoTracking()
            .Where(item => item.RuntimeId == runtimeId && item.Generation == runtime.Generation)
            .Select(item => item.WorkerNodeId).ToArrayAsync(token);
        nodeIds = nodeIds.Concat(assets.Where(item => item.WorkerNodeId.HasValue)
            .Select(item => item.WorkerNodeId!.Value)).Distinct().ToArray();
        var nodes = await context.WorkerNodes.AsNoTracking().Where(item => nodeIds.Contains(item.Id)).ToArrayAsync(token);
        var observedAt = DateTimeOffset.UtcNow;
        var inventories = await LoadInventoriesAsync(nodes, observedAt, token);
        // Recheck after the network reads: an operation can begin while a node responds.
        var active = (await LoadActiveTeamLabLifecycleOwnersAsync(token)).Contains(runtimeId);
        var items = new List<RuntimeResourceDifference>();
        var represented = new HashSet<(Guid, string)>();
        foreach (var asset in assets)
        {
            var desired = runtime.Status == TeamLabRuntimeStatus.Destroyed ? "absent" :
                asset.DesiredPowerState ?? (runtime.Status == TeamLabRuntimeStatus.Paused ? "paused" : "running");
            if (asset.WorkerNodeId is not { } nodeId || !inventories.TryGetValue(nodeId, out var inventory) ||
                inventory.Availability != InventoryAvailability.Available)
            {
                items.Add(new(asset.Id, asset.WorkerNodeId, asset.Kind.ToString().ToLowerInvariant(), asset.Name,
                    desired, null, "unavailable", null));
                continue;
            }
            var fact = ExpectedRuntimeFact.FromTeamLabAsset(asset);
            if (!inventory.Supports(fact.Kind))
            {
                items.Add(new(asset.Id, nodeId, fact.Kind.ToString().ToLowerInvariant(), asset.Name, desired, null, "unsupported", null));
                continue;
            }
            var actual = inventory.Find(fact);
            var conflict = inventory.FindIdentityConflict(fact);
            if (actual is not null) represented.Add((nodeId, actual.NativeId));
            if (conflict is not null) represented.Add((nodeId, conflict.NativeId));
            var identityConflict = conflict is not null && actual is null || actual is not null &&
                (actual.Generation != asset.Generation || actual.RuntimeId is { } owner && owner != runtimeId);
            var difference = active ? "busy" : identityConflict ? "identity-conflict" :
                desired == "absent" ? actual is null ? "matched" : "orphan" :
                actual is null ? "missing" : PowerMatches(fact, actual.State, desired) ? "matched" : "power-drift";
            var action = difference switch
            {
                "missing" when desired == "running" => "rebuild",
                "power-drift" when desired == "stopped" => "stop",
                "power-drift" when desired == "paused" && actual?.State == "running" => "pause",
                "power-drift" when desired == "running" => actual?.State == "paused" ? "resume" : "start",
                _ => null
            };
            items.Add(new(asset.Id, nodeId, fact.Kind.ToString().ToLowerInvariant(), asset.Name, desired, actual?.State,
                difference, action));
        }
        foreach (var inventory in inventories.Values.Where(item => item.Availability == InventoryAvailability.Available))
        foreach (var resource in inventory.AllResources().Where(item => item.Resource.RuntimeId == runtimeId))
        {
            if (represented.Contains((inventory.WorkerNodeId, resource.Resource.NativeId))) continue;
            items.Add(new(null, inventory.WorkerNodeId, resource.EventResourceType, resource.Resource.StableName,
                "absent", resource.Resource.State, active ? "busy" : "orphan", null));
        }
        var generation = await context.TeamLabRuntimes.AsNoTracking().Where(item => item.Id == runtimeId)
            .Select(item => item.Generation).SingleAsync(token);
        if (generation != runtime.Generation)
            return new(generation, observedAt, true, []);
        return new(runtime.Generation, observedAt, active, items);
    }

    private static bool PowerMatches(ExpectedRuntimeFact fact, string state, string? desired = null) =>
        (desired ?? fact.TeamLabAsset?.DesiredPowerState) switch
        {
            "stopped" => state is "exited" or "shutoff",
            "paused" => state == "paused",
            _ => IsActive(fact.Kind, state)
        };
}
