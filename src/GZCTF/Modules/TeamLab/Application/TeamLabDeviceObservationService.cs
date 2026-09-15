using System.Text.Json;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabDeviceObserver
{
    Task<TeamLabDeviceObservation?> ProbeAsync(Guid nodeId, TeamLabDeviceProbeRequest request, CancellationToken token);
}

public sealed class TeamLabDeviceObservationService(AppDbContext context, ITeamLabDeviceObserver observer,
    TeamLabEventRecorder events, TeamLabAuthorizationService authorization,
    TeamLabScopeAuthorizationService scopeAuthorization,
    IServiceScopeFactory? scopeFactory = null)
{
    public async Task<IReadOnlyList<TeamLabDeviceHealthModel>> ReadAsync(Guid runtimeId, Guid actorId, bool administrator, CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, TeamLabRuntimePermission.StateRead, token);
        return await ReadCoreAsync(runtimeId, token);
    }

    public async Task<IReadOnlyList<TeamLabDeviceHealthModel>> ReadApiAsync(
        Guid runtimeId,
        Guid apiTokenId,
        bool hasWildcardScopeGrant,
        CancellationToken token)
    {
        await scopeAuthorization.RequireRuntimeScopeAsync(
            runtimeId, apiTokenId, hasWildcardScopeGrant, writable: false, token);
        return await ReadCoreAsync(runtimeId, token);
    }

    private async Task<IReadOnlyList<TeamLabDeviceHealthModel>> ReadCoreAsync(Guid runtimeId, CancellationToken token)
    {
        var assets = await context.TeamLabRuntimeAssets.AsNoTracking().Include(asset => asset.Runtime).Where(asset => asset.Runtime.PublicId == runtimeId &&
            asset.Generation == asset.Runtime.Generation && asset.DevicePackageId != null).OrderBy(asset => asset.Id).ToArrayAsync(token);
        return assets.Select(asset =>
        {
            var observation = Decode(asset.DeviceObservationJson);
            var inactive = asset.Runtime.Status is TeamLabRuntimeStatus.Destroyed or TeamLabRuntimeStatus.Destroying or TeamLabRuntimeStatus.CleanupPending;
            if (inactive && observation is not null) observation = observation with { Status = "inactive" };
            return new TeamLabDeviceHealthModel(asset.Id, asset.Name, asset.Generation, observation, inactive ? null : asset.DeviceNextProbeAt);
        }).ToArray();
    }

    public async Task ScanAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await context.TeamLabRuntimeAssets.AsNoTracking().Where(asset => asset.DevicePackageId != null &&
            asset.Generation == asset.Runtime.Generation && (asset.Runtime.Status == TeamLabRuntimeStatus.Running || asset.Runtime.Status == TeamLabRuntimeStatus.Failed) &&
            (asset.DeviceNextProbeAt == null || asset.DeviceNextProbeAt <= now))
            .OrderBy(asset => asset.DeviceNextProbeAt).ThenBy(asset => asset.Id).Take(256)
            .Select(asset => new { asset.Id, asset.WorkerNodeId }).ToArrayAsync(token);
        if (candidates.Length == 0) return;
        if (scopeFactory is null)
        {
            foreach (var id in candidates.Select(item => item.Id)) await ObserveAsync(id, token);
            return;
        }
        var nodeGroups = candidates.GroupBy(item => item.WorkerNodeId).ToArray();
        await Parallel.ForEachAsync(nodeGroups, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(16, nodeGroups.Length),
            CancellationToken = token
        }, async (group, cancellationToken) =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<TeamLabDeviceObservationService>();
            await service.ObserveManyAsync(group.Select(item => item.Id), cancellationToken);
        });
    }

    internal async Task ObserveAsync(int assetId, CancellationToken token)
    {
        if (await ObserveCoreAsync(assetId, token))
            await context.SaveChangesAsync(token);
    }

    private async Task ObserveManyAsync(IEnumerable<int> assetIds, CancellationToken token)
    {
        var changed = false;
        foreach (var assetId in assetIds)
            changed |= await ObserveCoreAsync(assetId, token);
        if (changed)
            await context.SaveChangesAsync(token);
    }

    private async Task<bool> ObserveCoreAsync(int assetId, CancellationToken token)
    {
        var asset = await context.TeamLabRuntimeAssets.Include(item => item.Runtime).SingleAsync(item => item.Id == assetId, token);
        var runtime = asset.Runtime;
        if (asset.Generation != runtime.Generation || runtime.Status is not (TeamLabRuntimeStatus.Running or TeamLabRuntimeStatus.Failed)) return false;
        var snapshot = await context.TeamLabExecutionPlanSnapshots.AsNoTracking().SingleOrDefaultAsync(item =>
            item.RuntimeId == runtime.Id && item.Generation == asset.Generation && item.ShardId == asset.ShardId, token);
        TeamLabExecutionPlanV2? plan = null;
        try { if (snapshot is not null) plan = JsonSerializer.Deserialize<TeamLabExecutionPlanV2>(snapshot.PlanJson); }
        catch (JsonException) { }
        var device = plan?.Assets.SingleOrDefault(item => item.AssetKey == asset.TopologyKey)?.Device;
        var interval = device?.HealthIntervalSeconds ?? 30;
        var generation = asset.Generation;
        var identity = (asset.RuntimeResourceId, asset.NativeIdentity, asset.WorkerNodeId);
        TeamLabDeviceObservation observation;
        if (asset.DesiredPowerState is "stopped" or "paused") observation = new("stopped", DateTimeOffset.UtcNow);
        else if (plan is null || !plan.IsValid(out _) || device is null || asset.RuntimeResourceId is null || asset.WorkerNodeId != snapshot!.WorkerNodeId)
            observation = new("unavailable", DateTimeOffset.UtcNow, "device.execution_snapshot_missing");
        else
        {
            try
            {
                observation = await observer.ProbeAsync(snapshot.WorkerNodeId,
                    new(plan, asset.TopologyKey, asset.RuntimeResourceId, asset.NativeIdentity), token)
                    ?? new("unavailable", DateTimeOffset.UtcNow, "device.node_unavailable");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                observation = new("unavailable", DateTimeOffset.UtcNow, "device.node_unavailable");
            }
        }
        await context.Entry(asset).ReloadAsync(token);
        await context.Entry(runtime).ReloadAsync(token);
        if (asset.Generation != generation || runtime.Generation != generation ||
            (asset.RuntimeResourceId, asset.NativeIdentity, asset.WorkerNodeId) != identity ||
            runtime.Status is not (TeamLabRuntimeStatus.Running or TeamLabRuntimeStatus.Failed)) return false;
        if (asset.DesiredPowerState is "stopped" or "paused") observation = new("stopped", DateTimeOffset.UtcNow);
        var previous = Decode(asset.DeviceObservationJson);
        if (previous is not null && previous.BootId == observation.BootId && observation.ProtocolCounters is { } current &&
            current.Any(pair => pair.Value < (previous.ProtocolCounters?.GetValueOrDefault(pair.Key) ?? 0)))
            observation = new("unhealthy", observation.ObservedAt, "device.counter_regressed");
        if (observation.Status != previous?.Status)
            events.Record(runtime, "device-health", observation.Status == "healthy" ? TeamLabEventLevel.Success : TeamLabEventLevel.Warning,
                OperationalEventCodes.TeamLab.DeviceHealthChanged, OperationalEventOutcome.Succeeded,
                $"设备 {asset.Name} 健康状态：{HealthName(observation.Status)}");
        if (observation.ProtocolCounters is { } counters)
        foreach (var pair in counters)
        {
            var baseline = previous is not null && previous.BootId == observation.BootId ? previous.ProtocolCounters?.GetValueOrDefault(pair.Key) ?? 0 : 0;
            if (pair.Value <= baseline) continue;
            events.Record(runtime, "protocol", TeamLabEventLevel.Info, OperationalEventCodes.TeamLab.ProtocolEvent,
                OperationalEventOutcome.Succeeded, $"设备 {asset.Name}：{pair.Key}，新增 {pair.Value - baseline} 次",
                detail: new Dictionary<string, object?> { ["protocolEventType"] = pair.Key, ["protocolEventSource"] = asset.TopologyKey,
                    ["protocolEventParameterCount"] = 0 });
        }
        else if (previous is not null)
            observation = observation with { BootId = previous.BootId, ProtocolCounters = previous.ProtocolCounters };
        asset.DeviceObservationJson = JsonSerializer.Serialize(observation);
        asset.DeviceNextProbeAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(interval, 1, 3600));
        return true;
    }

    static TeamLabDeviceObservation? Decode(string? json)
    {
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<TeamLabDeviceObservation>(json); }
        catch (JsonException) { return null; }
    }

    static string HealthName(string status) => status switch
    {
        "healthy" => "正常", "unhealthy" => "异常", "unavailable" => "未能确认",
        "stopped" => "已停止或暂停", "not-configured" => "未配置健康检查", _ => "未知"
    };
}
