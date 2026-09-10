using System.Collections.Concurrent;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

public sealed partial class TeamLabExecutionPlanExecutor
{
    public async Task<TeamLabAssetControlResult> ControlAssetAsync(TeamLabAssetControlRequest request, CancellationToken token)
    {
        var plan = request.Plan;
        if (plan is null || !plan.IsValid(out _) || request.Action is not ("start" or "stop" or "pause" or "resume" or "remove" or "create") ||
            plan.Assets.SingleOrDefault(item => item.AssetKey == request.AssetKey) is not { } asset)
            return new(false, "asset_control.invalid_request", null);
        using var executionLock = await executionLocks.AcquireAsync((plan.RuntimeId, plan.Generation, plan.ShardKey), token);
        if (journal.TryGetIdentity(plan, out var digest) && !string.Equals(digest, plan.PlanDigest, StringComparison.OrdinalIgnoreCase))
            return new(false, "asset_control.identity_conflict", null);
        async Task<TeamLabExecutionInventoryFactV2?> Observe() =>
            (await ReadInventoryAsync(plan with { Assets = [asset] }, token)).SingleOrDefault(item => item.AssetKey == asset.AssetKey);
        var before = await Observe();
        if (request.Action is not "create" && before is not null &&
            (before.ResourceId != request.ExpectedResourceId || request.ExpectedNativeIdentity is not null &&
                before.NativeIdentity != request.ExpectedNativeIdentity))
            return new(false, "asset_control.identity_conflict", before);
        if (before is null && request.Action is not ("create" or "remove"))
            return new(false, "asset_control.resource_missing", null);
        var events = new ConcurrentQueue<TeamLabExecutionEventV2>();
        try
        {
            if (before is not null)
            {
                if (asset.Kind == "docker")
                    await docker.ControlTeamLabPowerAsync(before.ResourceId, plan.RuntimeId, plan.Generation, plan.PlanDigest, "inspect", token);
                else if (!(await libvirt.ChangePowerAsync(plan, asset, "inspect", request.ExpectedNativeIdentity, token)).Success)
                    return new(false, "asset_control.identity_conflict", before);
            }
            switch (request.Action)
            {
                case "remove":
                    await CleanupAssetAsync(plan, asset, before is null ? [] : [before], events, token);
                    if (asset.Kind == "docker")
                        foreach (var attachment in asset.NetworkAttachments)
                        {
                            var local = await linuxNetwork.RemoveContainerAttachmentAsync(plan, asset.AssetKey, attachment.NetworkKey, token);
                            var remote = await ovs.RemoveAsync(plan,
                                LinuxNetworkAttachmentService.HostInterfaceName(plan, asset.AssetKey, attachment.NetworkKey), attachment.NetworkKey, token);
                            if (!local.Success || !remote.Success)
                                return new(false, "asset_control.attachment_cleanup_failed", await Observe());
                        }
                    break;
                case "start":
                case "create":
                    if (asset.Kind == "docker")
                    {
                        if (before?.State == "paused")
                            await docker.ControlTeamLabPowerAsync(before.ResourceId, plan.RuntimeId, plan.Generation, plan.PlanDigest, "resume", token);
                        await ApplyDockerAsync(plan, asset, events, token, preserveContainer: before is not null);
                    }
                    else await ApplyVmAsync(plan, asset, events, token);
                    break;
                default:
                    if (asset.Kind == "docker")
                        await docker.ControlTeamLabPowerAsync(before!.ResourceId, plan.RuntimeId, plan.Generation, plan.PlanDigest, request.Action, token);
                    else
                    {
                        var response = await libvirt.ChangePowerAsync(plan, asset, request.Action, request.ExpectedNativeIdentity, token);
                        if (!response.Success) return new(false, "asset_control.power_failed", await Observe());
                    }
                    break;
            }
            var after = await Observe();
            var expected = request.Action switch { "stop" => "stopped", "pause" => "paused", "remove" => "missing", _ => "running" };
            var observed = after?.State switch { null => "missing", "shutoff" or "exited" => "stopped", var value => value };
            return new(expected == observed && events.All(item => item.Outcome != "failed"),
                expected == observed && events.All(item => item.Outcome != "failed") ? null : "asset_control.state_not_reached", after);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning("TeamLab asset control failed: runtime={RuntimeId} generation={Generation} asset={AssetKey} action={Action} type={ErrorType}",
                plan.RuntimeId, plan.Generation, asset.AssetKey, request.Action, error.GetType().Name);
            return new(false, "asset_control.execution_failed", null);
        }
    }
}
