using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application.Rollouts;

/// <summary>
/// Supplies rollout targets from the caller's immutable snapshot. It deliberately
/// has no dependency on a business module such as Penetration.
/// </summary>
public sealed class TeamLabExternalRolloutProvider(
    AppDbContext context,
    ITeamLabRuntimeApplicationService runtimes) : ITeamLabRolloutTargetProvider
{
    public string AdapterKind => "external";

    public Task SynchronizeTargetsAsync(TeamLabRollout rollout, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<TeamLabRolloutProvisionResult> ProvisionAsync(
        TeamLabRollout rollout,
        TeamLabRolloutTarget target,
        CancellationToken cancellationToken)
    {
        var releaseOwner = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.Id == rollout.ReleaseId)
            .Select(item => new { OwnerUserId = item.Topology.OwnerUserId, item.ControlScopeId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑 release", 404);
        if (releaseOwner.ControlScopeId != rollout.ControlScopeId)
            throw new TeamLabApiContractException("rollout_scope_mismatch", "rollout 与 release 属于不同的 control scopes", 409);

        var owner = releaseOwner.OwnerUserId ?? rollout.CreatedByUserId;
        var command = new CreateTeamLabRuntimeModel(
            rollout.ReleaseId,
            $"teamlab:rollout:{rollout.PublicId:D}:target:{target.PublicId:D}",
            null,
            null);
        var requestHash = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { rollout.ReleaseId, target.PublicId, target.ExternalSubject })))}";
        var result = await runtimes.PlanAndEnqueueAsync(
            command,
            rollout.CreatedByUserId,
            owner,
            requestHash,
            $"teamlab-rollout-target-{target.PublicId:N}",
            null,
            target.DisplayName,
            cancellationToken);
        return new TeamLabRolloutProvisionResult(result.RuntimeId, result.RuntimePublicId, null);
    }
}
