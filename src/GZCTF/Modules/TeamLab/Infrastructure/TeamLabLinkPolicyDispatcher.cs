using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Infrastructure;

/// <summary>
/// Realizes link policies on the worker node that hosts the runtime. Resolves
/// the node from the runtime's active shard (falling back to its assets) and
/// the managed asset link, then asks the Agent to actually apply tc netem /
/// ip-link damage on the host-side veth and to recover it. Returns the real
/// data-plane result so the control plane never reports an unrealized policy.
/// </summary>
public sealed class TeamLabLinkPolicyDispatcher(AgentClient agent) : ITeamLabLinkPolicyDispatcher
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
        var network = runtime.Networks.FirstOrDefault(item => item.TopologyKey == networkKey);
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
                GatewayIp: network?.GatewayIp),
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
        CancellationToken cancellationToken)
    {
        var dispatch = Resolve(runtime, assetKey);
        if (dispatch is null)
            return new TeamLabLinkPolicyDispatchResult(false, "运行时没有可用的执行节点");
        var network = runtime.Networks.FirstOrDefault(item => item.TopologyKey == networkKey);
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
                GatewayIp: network?.GatewayIp),
            cancellationToken);
        if (response is null)
            return new TeamLabLinkPolicyDispatchResult(false, "Agent 未返回链路策略恢复结果");
        return new TeamLabLinkPolicyDispatchResult(response.Success, response.Message);
    }

    private static (Guid NodeId, string AssetKey, Guid RuntimePublicId, int Generation, int ShardId)? Resolve(
        TeamLabRuntime runtime,
        string assetKey)
    {
        var shard = runtime.Shards
            .FirstOrDefault(shard => shard.Status is not (TeamLabRuntimeStatus.Destroyed
                or TeamLabRuntimeStatus.Destroying or TeamLabRuntimeStatus.CleanupPending));
        var nodeId = shard?.WorkerNodeId;
        nodeId ??= runtime.Assets.Select(asset => (Guid?)asset.WorkerNodeId).FirstOrDefault();
        if (nodeId is null || string.IsNullOrWhiteSpace(assetKey)) return null;

        var generation = shard?.Generation ?? runtime.Generation;
        var shardId = shard?.Id ?? 0;
        if (generation <= 0) generation = runtime.Generation;
        return (nodeId.Value, assetKey, runtime.PublicId, generation, shardId);
    }
}
