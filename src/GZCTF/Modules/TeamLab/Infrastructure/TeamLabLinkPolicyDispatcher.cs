using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GZCTF.Modules.TeamLab.Infrastructure;

/// <summary>
/// Realizes link policies on the worker node that hosts the runtime. Resolves
/// the node from the selected asset's current-generation shard and
/// the managed asset link, then asks the Agent to actually apply tc netem /
/// ip-link damage on the host-side veth and to recover it. Returns the real
/// data-plane result so the control plane never reports an unrealized policy.
/// </summary>
public sealed class TeamLabLinkPolicyDispatcher(AgentClient agent, AppDbContext context) : ITeamLabLinkPolicyDispatcher
{
    public async Task<TeamLabLinkPolicyDispatchResult> ApplyAsync(
        TeamLabRuntime runtime,
        string networkKey,
        string assetKey,
        string kind,
        string parameters,
        CancellationToken cancellationToken)
    {
        var dispatch = Resolve(runtime, assetKey);
        if (dispatch is null)
            return new TeamLabLinkPolicyDispatchResult(false, "运行时没有可用的执行节点");
        var network = runtime.Networks.FirstOrDefault(item => item.Generation == runtime.Generation && item.TopologyKey == networkKey);
        var digest = kind == "nat" ? await NetworkDigestAsync(runtime, dispatch.Value.ShardId, cancellationToken) : null;
        if (kind == "nat" && digest is null) return new(false, "缺少原始网络执行快照，无法确认 NAT 资源归属。");
        var response = await agent.ApplyTeamLabLinkPolicyAsync(
            dispatch.Value.NodeId,
            new TeamLabLinkPolicyApplyRequest(
                dispatch.Value.RuntimePublicId,
                dispatch.Value.Generation,
                networkKey,
                dispatch.Value.AssetKey,
                kind,
                parameters,
                RuntimeId: runtime.Id,
                RouterNamespace: TeamLabResourceNameFactory.RouterNamespace(runtime.Id, dispatch.Value.ShardId),
                NetworkCidr: network?.Cidr,
                GatewayIp: network?.GatewayIp,
                NetworkDigest: digest),
            cancellationToken);
        if (response is null)
            return new TeamLabLinkPolicyDispatchResult(false, "Agent 未返回链路策略结果");
        return new TeamLabLinkPolicyDispatchResult(response.Success, response.Message);
    }

    public async Task<TeamLabLinkPolicyDispatchResult> RecoverAsync(
        TeamLabRuntime runtime,
        string networkKey,
        string assetKey,
        string kind,
        string? parameters,
        CancellationToken cancellationToken)
    {
        var dispatch = Resolve(runtime, assetKey);
        if (dispatch is null)
            return new TeamLabLinkPolicyDispatchResult(false, "运行时没有可用的执行节点");
        var network = runtime.Networks.FirstOrDefault(item => item.Generation == runtime.Generation && item.TopologyKey == networkKey);
        var digest = kind == "nat" ? await NetworkDigestAsync(runtime, dispatch.Value.ShardId, cancellationToken) : null;
        if (kind == "nat" && digest is null) return new(false, "缺少原始网络执行快照，无法确认 NAT 资源归属。");
        var response = await agent.RecoverTeamLabLinkPolicyAsync(
            dispatch.Value.NodeId,
            new TeamLabLinkPolicyRecoverRequest(
                dispatch.Value.RuntimePublicId,
                dispatch.Value.Generation,
                networkKey,
                dispatch.Value.AssetKey,
                kind,
                RuntimeId: runtime.Id,
                RouterNamespace: TeamLabResourceNameFactory.RouterNamespace(runtime.Id, dispatch.Value.ShardId),
                NetworkCidr: network?.Cidr,
                GatewayIp: network?.GatewayIp,
                ParametersJson: parameters,
                NetworkDigest: digest),
            cancellationToken);
        if (response is null)
            return new TeamLabLinkPolicyDispatchResult(false, "Agent 未返回链路策略恢复结果");
        return new TeamLabLinkPolicyDispatchResult(response.Success, response.Message);
    }

    private async Task<string?> NetworkDigestAsync(TeamLabRuntime runtime, int shardId, CancellationToken token)
    {
        var json = await context.TeamLabExecutionPlanSnapshots.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation && item.ShardId == shardId)
            .Select(item => item.PlanJson).SingleOrDefaultAsync(token);
        var plan = json is null ? null : JsonSerializer.Deserialize<TeamLabExecutionPlanV2>(json);
        return plan is not null && plan.IsValid(out _) ? plan.NetworkDigest : null;
    }

    internal static (Guid NodeId, string AssetKey, Guid RuntimePublicId, int Generation, int ShardId)? Resolve(
        TeamLabRuntime runtime,
        string assetKey)
    {
        var asset = runtime.Assets.SingleOrDefault(item => item.Generation == runtime.Generation && item.TopologyKey == assetKey);
        if (asset?.WorkerNodeId is not { } nodeId || asset.ShardId is not { } shardId) return null;
        var shard = runtime.Shards.SingleOrDefault(item => item.Id == shardId && item.Generation == runtime.Generation && item.WorkerNodeId == nodeId);
        if (shard is null) return null;
        return (nodeId, assetKey, runtime.PublicId, runtime.Generation, shardId);
    }
}
