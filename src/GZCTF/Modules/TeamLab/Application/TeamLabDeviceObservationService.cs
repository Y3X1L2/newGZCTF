using System.Text.Json;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabDeviceObserver
{
    Task<TeamLabDeviceObservation?> ProbeAsync(Guid nodeId, TeamLabDeviceProbeRequest request, CancellationToken token);
}

public sealed record TeamLabDeviceHealthModel(int AssetId, string Name, int Generation, TeamLabDeviceObservation? Observation, DateTimeOffset? NextProbeAt);

public sealed class TeamLabDeviceObservationService(AppDbContext context, ITeamLabDeviceObserver observer,
    TeamLabEventRecorder events, TeamLabAuthorizationService authorization)
{
    public async Task<IReadOnlyList<TeamLabDeviceHealthModel>> ReadAsync(Guid runtimeId, Guid actorId, bool administrator, CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, TeamLabRuntimePermission.StateRead, token);
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
        var ids = await context.TeamLabRuntimeAssets.AsNoTracking().Where(asset => asset.DevicePackageId != null &&
            asset.Generation == asset.Runtime.Generation && (asset.Runtime.Status == TeamLabRuntimeStatus.Running || asset.Runtime.Status == TeamLabRuntimeStatus.Failed) &&
            (asset.DeviceNextProbeAt == null || asset.DeviceNextProbeAt <= now))
            .OrderBy(asset => asset.DeviceNextProbeAt).ThenBy(asset => asset.Id).Take(20).Select(asset => asset.Id).ToArrayAsync(token);
        foreach (var id in ids) await ObserveAsync(id, token);
    }

    internal async Task ObserveAsync(int assetId, CancellationToken token)
    {
        var asset = await context.TeamLabRuntimeAssets.Include(item => item.Runtime).SingleAsync(item => item.Id == assetId, token);
        var runtime = asset.Runtime;
        if (asset.Generation != runtime.Generation || runtime.Status is not (TeamLabRuntimeStatus.Running or TeamLabRuntimeStatus.Failed)) return;
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
            runtime.Status is not (TeamLabRuntimeStatus.Running or TeamLabRuntimeStatus.Failed)) return;
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
        await context.SaveChangesAsync(token);
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
