using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using Microsoft.EntityFrameworkCore;
using NetlabEnvironment = GZCTF.Modules.TeamLab.Infrastructure.Netlab.Environment;
using NetlabStatus = GZCTF.Modules.TeamLab.Infrastructure.Netlab.EnvironmentStatus;

namespace GZCTF.Modules.Penetration.Application;

public sealed class PenetrationTeamLabAdapter(AppDbContext context, NetlabClient netlab,
    NetlabConnection connection, NetlabWorkspaceAccess access, PenetrationObjectiveService objectives,
    IDistributedLeaseProvider locks)
{
    public async Task<PenetrationGameLabBindingModel?> GetBindingAsync(int gameId, CancellationToken token)
    {
        var binding = await context.PenetrationGameLabBindings.AsNoTracking().SingleOrDefaultAsync(x => x.GameId == gameId, token);
        return binding?.BlueprintId is null ? null : new(gameId, binding.BlueprintId, binding.BlueprintVersionId,
            binding.MaxResetCount, binding.ObjectiveRevision, await objectives.ListAsync(gameId, token));
    }

    public async Task<PenetrationGameLabBindingModel> BindAsync(int gameId, string blueprintId,
        Guid actorUserId, CancellationToken token)
    {
        await netlab.GetAsync<Blueprint>($"blueprints/{Uri.EscapeDataString(blueprintId)}", token, await ManagerScopeAsync(actorUserId, token));
        await using var lease = await locks.AcquireAsync(PenetrationObjectiveService.ConfigurationLeaseKey(gameId),
            TimeSpan.FromSeconds(10), cancellationToken: token);
        var binding = await context.PenetrationGameLabBindings.SingleOrDefaultAsync(x => x.GameId == gameId, token);
        if (binding is null)
        {
            binding = new() { GameId = gameId, BlueprintId = blueprintId };
            context.PenetrationGameLabBindings.Add(binding);
        }
        else if (binding.BlueprintId != blueprintId)
        {
            await RequireEmptyAsync(gameId, token);
            if (await context.PenetrationSubmissions.AnyAsync(x => x.GameId == gameId, token))
                throw Conflict("比赛已有提交记录，场景与得分目标需要保持一致。");
            context.PenetrationObjectives.RemoveRange(await context.PenetrationObjectives.Where(x => x.GameId == gameId).ToArrayAsync(token));
            binding.BlueprintId = blueprintId;
            binding.BlueprintVersionId = null;
            binding.ObjectiveRevision++;
        }
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        return (await GetBindingAsync(gameId, token))!;
    }

    public async Task<PenetrationGameLabBindingModel> ActivateReleaseAsync(int gameId, string versionId,
        Guid actorUserId, CancellationToken token)
    {
        var version = await netlab.GetAsync<BlueprintVersion>($"blueprint-versions/{Uri.EscapeDataString(versionId)}",
            token, await ManagerScopeAsync(actorUserId, token));
        await using var lease = await locks.AcquireAsync(PenetrationObjectiveService.ConfigurationLeaseKey(gameId),
            TimeSpan.FromSeconds(10), cancellationToken: token);
        var binding = await SettingsAsync(gameId, token);
        if (version.BlueprintId != binding.BlueprintId) throw Conflict("请选择当前场景的版本。");
        if (binding.BlueprintVersionId != versionId) await RequireEmptyAsync(gameId, token);
        binding.BlueprintVersionId = versionId;
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        return (await GetBindingAsync(gameId, token))!;
    }

    public async Task<IReadOnlyList<PenetrationReleaseOptionModel>> ListAvailableReleasesAsync(
        Guid actorUserId, CancellationToken token)
    {
        var scope = await ManagerScopeAsync(actorUserId, token);
        var blueprints = await netlab.ListAsync<Blueprint>("blueprints", x => x.Id, token, scope);
        List<PenetrationReleaseOptionModel> result = [];
        foreach (var blueprint in blueprints)
        {
            var versions = await netlab.ListAsync<BlueprintVersionSummary>($"blueprints/{Uri.EscapeDataString(blueprint.Id)}/versions", x => x.Id, token, scope);
            result.AddRange(versions.Select(v => new PenetrationReleaseOptionModel(blueprint.Id, blueprint.Name,
                v.Id, v.Version, v.NetworkCount, v.AssetCount, v.CreatedAt)));
        }
        return result;
    }

    public async Task<IReadOnlyList<PenetrationSceneAsset>> ListAssetsAsync(int gameId, CancellationToken token)
    {
        var binding = await SettingsAsync(gameId, token);
        if (binding.BlueprintVersionId is null) return [];
        var version = await netlab.GetAsync<BlueprintVersion>($"blueprint-versions/{Uri.EscapeDataString(binding.BlueprintVersionId)}", token);
        var ids = string.Join(",", version.Spec.Assets.Select(a => a.TemplateId).Distinct());
        if (ids.Length == 0) return [];
        var templates = (await netlab.ListAsync<Template>($"templates?ids={Uri.EscapeDataString(ids)}", x => x.Id, token)).ToDictionary(x => x.Id);
        return version.Spec.Assets.Select(a => new PenetrationSceneAsset(a.Id, a.Name, templates[a.TemplateId].Kind.ToString().ToLowerInvariant())).ToArray();
    }

    public async Task<PenetrationGameTeamLabModel> GetGameTeamLabAsync(int gameId, CancellationToken token)
    {
        var binding = await GetBindingAsync(gameId, token);
        return new(binding, binding?.ActiveReleaseId is null ? null : await SummaryAsync(gameId, token));
    }

    public async Task<PenetrationSceneSummary> PrepareAsync(int gameId, CancellationToken token)
    {
        var teams = await context.Participations.AsNoTracking().Where(x => x.GameId == gameId && x.Status == ParticipationStatus.Accepted)
            .Select(x => x.TeamId).Distinct().Order().ToArrayAsync(token);
        foreach (var team in teams) await DeployTeamAsync(gameId, team, token);
        return await SummaryAsync(gameId, token);
    }

    public async Task<PenetrationSceneSummary> SetAccessAsync(int gameId, bool open, CancellationToken token)
    {
        var binding = await SettingsAsync(gameId, token);
        binding.AccessOpen = open;
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        return await SummaryAsync(gameId, token);
    }

    public Task<bool> IsPlayerAccessOpenAsync(int gameId, CancellationToken token) =>
        context.PenetrationGameLabBindings.AnyAsync(x => x.GameId == gameId && x.AccessOpen, token);
    public Task<PenetrationSceneSummary> DrainAsync(int gameId, CancellationToken token) =>
        ActOnGameAsync(gameId, ActionRequestAction.Destroy, token);
    public Task<PenetrationSceneSummary> PauseAsync(int gameId, CancellationToken token) =>
        ActOnGameAsync(gameId, ActionRequestAction.Suspend, token);
    public Task<PenetrationSceneSummary> ResumeAsync(int gameId, CancellationToken token) =>
        ActOnGameAsync(gameId, ActionRequestAction.Resume, token);

    public async Task<PenetrationDeploymentResult> DeployTeamAsync(int gameId, int teamId, CancellationToken token)
    {
        await using var lease = await locks.AcquireAsync($"penetration:environment:{gameId}:{teamId}", TimeSpan.FromSeconds(30), cancellationToken: token);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = lifetime.Token;
        var settings = await SettingsAsync(gameId, token);
        if (settings.BlueprintVersionId is null) throw Conflict("请先选择场景版本。");
        var binding = await context.PenetrationTeamRuntimeBindings.SingleOrDefaultAsync(x => x.GameId == gameId && x.TeamId == teamId, token);
        if (binding?.EnvironmentId is { } existing)
        {
            var state = await StateAsync(existing, token);
            if (state.Status != "destroyed")
            {
                if (state.Status == "stopped") await ActionAsync(existing, state.Revision, ActionRequestAction.Start, token);
                return new(existing, true);
            }
            binding.CreationRequestId = Guid.NewGuid();
            binding.EnvironmentId = null;
        }
        if (binding is null)
        {
            binding = new() { GameId = gameId, TeamId = teamId, CreationRequestId = Guid.NewGuid() };
            context.PenetrationTeamRuntimeBindings.Add(binding);
        }
        var version = await netlab.GetAsync<BlueprintVersion>($"blueprint-versions/{Uri.EscapeDataString(settings.BlueprintVersionId)}", token);
        var flags = await objectives.BuildOverlaysAsync(gameId, teamId, version.Version, token);
        var templateIds = string.Join(",", version.Spec.Assets.Select(x => x.TemplateId).Distinct());
        var templates = (await netlab.ListAsync<Template>($"templates?ids={Uri.EscapeDataString(templateIds)}", x => x.Id, token)).ToDictionary(x => x.Id);
        foreach (var asset in version.Spec.Assets)
            if (flags.TryGetValue(asset.Id, out var values))
            {
                var template = templates[asset.TemplateId];
                if (template.Kind == TemplateKind.Container)
                {
                    asset.Parameters ??= [];
                    foreach (var (key, value) in values) asset.Parameters[key] = value;
                }
                else
                {
                    asset.Guest ??= new();
                    asset.Guest.Files ??= [];
                    var directory = string.Equals(template.Os, "windows", StringComparison.OrdinalIgnoreCase)
                        ? @"C:\ProgramData\GZCTF\Runtime\secrets\" : "/opt/gzctf/runtime/secrets/";
                    foreach (var (key, value) in values) asset.Guest.Files[directory + key] = value;
                }
            }
        binding.BlueprintVersionId = version.Id;
        binding.BlueprintVersion = version.Version;
        binding.DestroyedAt = null;
        await context.SaveChangesAsync(token);
        var names = await context.Participations.AsNoTracking().Where(x => x.GameId == gameId && x.TeamId == teamId)
            .Select(x => new { Game = x.Game.Title, Team = x.Team.Name }).FirstAsync(token);
        var environment = await netlab.SendAsync<NetlabEnvironment>(HttpMethod.Post, "environments", new CreateEnvironment
        {
            Name = $"{names.Game} · {names.Team}", ProjectId = GameProject(gameId),
            ExternalReference = $"game:{gameId}/team:{teamId}", ClientRequestId = binding.CreationRequestId.ToString(),
            Spec = version.Spec, Run = true
        }, token);
        binding.EnvironmentId = environment.Id;
        await context.SaveChangesAsync(token);
        return new(environment.Id, false);
    }

    public async Task<PenetrationDeploymentResult> ResetTeamAsync(int gameId, int teamId, Guid userId, bool byAdmin, CancellationToken token)
    {
        await using var lease = await locks.AcquireAsync($"penetration:environment:{gameId}:{teamId}", TimeSpan.FromSeconds(30), cancellationToken: token);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = lifetime.Token;
        var binding = await RuntimeAsync(gameId, teamId, token);
        var state = await StateAsync(binding.EnvironmentId!, token);
        var settings = await SettingsAsync(gameId, token);
        var resets = await RefreshResetsAsync(binding.EnvironmentId!, token);
        var pending = resets.SingleOrDefault(x => x.Status is PenetrationResetStatus.Pending or PenetrationResetStatus.Running);
        if (pending?.RemoteOperationId is not null) return new(binding.EnvironmentId!, true);
        if (pending is null)
        {
            if (!byAdmin && resets.Count(x => !x.ByAdmin && x.Status != PenetrationResetStatus.Failed) >= settings.MaxResetCount)
                throw Conflict("本队重建次数已用完。");
            pending = new PenetrationResetRecord
            {
                EnvironmentId = binding.EnvironmentId, OperationId = Guid.NewGuid(), TargetGeneration = state.Revision,
                UserId = userId, ByAdmin = byAdmin
            };
            context.PenetrationResetRecords.Add(pending);
            await context.SaveChangesAsync(token);
        }
        Operation operation;
        try
        {
            operation = await netlab.SendAsync<Operation>(HttpMethod.Post, $"environments/{Uri.EscapeDataString(binding.EnvironmentId!)}/actions",
                new ActionRequest { Action = ActionRequestAction.Rebuild, ExpectedRevision = pending.TargetGeneration,
                    ResetVolumes = true, ClientRequestId = pending.OperationId.ToString() }, token);
        }
        catch (TeamLabApiContractException error) when (error.StatusCode is >= 400 and < 500)
        {
            pending.Status = PenetrationResetStatus.Failed;
            pending.CompletedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);
            throw;
        }
        pending.RemoteOperationId = operation.Id;
        pending.Status = PenetrationResetStatus.Running;
        await context.SaveChangesAsync(token);
        return new(binding.EnvironmentId!, false);
    }

    public async Task DestroyTeamAsync(int gameId, int teamId, CancellationToken token)
    {
        var binding = await RuntimeAsync(gameId, teamId, token);
        var state = await StateAsync(binding.EnvironmentId!, token);
        if (state.Status != "destroyed") await ActionAsync(binding.EnvironmentId!, state.Revision, ActionRequestAction.Destroy, token);
    }

    public async Task DestroyGameAsync(int gameId, CancellationToken token) =>
        await ActOnGameAsync(gameId, ActionRequestAction.Destroy, token);

    public async Task RetireExpiredAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await context.PenetrationTeamRuntimeBindings
            .Where(binding => binding.EnvironmentId != null && binding.DestroyedAt == null &&
                (context.Games.Any(game => game.Id == binding.GameId && !game.PracticeMode && game.EndTimeUtc <= now) ||
                 !context.Participations.Any(p => p.GameId == binding.GameId && p.TeamId == binding.TeamId && p.Status == ParticipationStatus.Accepted)))
            .ToArrayAsync(token);
        foreach (var group in expired.GroupBy(binding => binding.GameId))
        {
            var environments = (await EnvironmentsAsync(group.Key, token)).ToDictionary(e => e.Id);
            foreach (var binding in group)
            {
                if (!environments.TryGetValue(binding.EnvironmentId!, out var environment) || environment.Status == NetlabStatus.Destroyed)
                    binding.DestroyedAt = now;
                else if (environment.Status != NetlabStatus.Destroying)
                    await ActionAsync(environment.Id, environment.Revision, ActionRequestAction.Destroy, token);
            }
            await context.SaveChangesAsync(token);
        }
    }

    public async Task EnsureGameDrainedBeforeDeleteAsync(int gameId, CancellationToken token)
    {
        if (!await context.PenetrationGameLabBindings.AnyAsync(x => x.GameId == gameId && x.BlueprintId != null, token)) return;
        var remaining = (await EnvironmentsAsync(gameId, token)).Where(x => x.Status != NetlabStatus.Destroyed).ToArray();
        if (remaining.Length == 0) return;
        foreach (var environment in remaining.Where(x => x.Status != NetlabStatus.Destroying))
            await ActionAsync(environment.Id, environment.Revision, ActionRequestAction.Destroy, token);
        throw Conflict("比赛环境正在清理，完成后即可删除比赛。");
    }

    public async Task<IReadOnlyList<PenetrationRuntimeBindingModel>> ListRuntimesAsync(int gameId, CancellationToken token)
    {
        var bindings = await context.PenetrationTeamRuntimeBindings.AsNoTracking().Where(x => x.GameId == gameId && x.EnvironmentId != null)
            .Select(x => new { x.TeamId, x.EnvironmentId, x.BlueprintVersion,
                Name = context.Teams.Where(t => t.Id == x.TeamId).Select(t => t.Name).First() }).ToArrayAsync(token);
        var environments = (await EnvironmentsAsync(gameId, token)).ToDictionary(x => x.Id);
        return bindings.Where(x => environments.ContainsKey(x.EnvironmentId!)).Select(x =>
        {
            var environment = environments[x.EnvironmentId!];
            return new PenetrationRuntimeBindingModel(x.TeamId, x.Name, environment.Id, x.BlueprintVersion,
                Status(environment.Status.ToString().ToLowerInvariant()), environment.Status.ToString().ToLowerInvariant(),
                environment.AssetCount, environment.CreatedAt, environment.UpdatedAt, null);
        }).ToArray();
    }

    public async Task<PenetrationTargetPage> ListRolloutTargetsAsync(int gameId, string? after, int limit, CancellationToken token)
    {
        if (after is not null && !int.TryParse(after, out _)) throw new TeamLabApiContractException("invalid_cursor", "分页位置无效。", 400);
        limit = Math.Clamp(limit, 1, 100);
        var rows = (await ListRuntimesAsync(gameId, token)).OrderBy(x => x.TeamId)
            .Where(x => after is null || x.TeamId > int.Parse(after)).Take(limit + 1).ToArray();
        return new(rows.Take(limit).Select(x => new PenetrationSceneTarget(x.RuntimeId, $"team:{x.TeamId}", x.TeamName, x.RuntimeId,
            x.Stage, null, x.Status, x.Stage,
            x.CreatedAt, x.UpdatedAt ?? x.CreatedAt, x.Error)).ToArray(), rows.Length > limit ? rows[limit - 1].TeamId.ToString() : null);
    }

    public async Task<List<PenetrationResetRecord>> RefreshResetsAsync(string environmentId, CancellationToken token)
    {
        var rows = await context.PenetrationResetRecords.Where(x => x.EnvironmentId == environmentId).ToListAsync(token);
        foreach (var row in rows.Where(x => x.RemoteOperationId != null && x.Status is PenetrationResetStatus.Pending or PenetrationResetStatus.Running))
        {
            var operation = await netlab.GetAsync<Operation>($"operations/{Uri.EscapeDataString(row.RemoteOperationId!)}", token);
            if (operation.State is OperationState.Queued or OperationState.Running) continue;
            row.Status = operation.State == OperationState.Succeeded ? PenetrationResetStatus.Succeeded : PenetrationResetStatus.Failed;
            row.CompletedAt = operation.UpdatedAt;
        }
        await context.SaveChangesAsync(token);
        return rows;
    }

    private async Task<PenetrationSceneSummary> ActOnGameAsync(int gameId, ActionRequestAction action, CancellationToken token)
    {
        if (action == ActionRequestAction.Destroy)
        {
            var settings = await SettingsAsync(gameId, token);
            settings.AccessOpen = false;
            await context.SaveChangesAsync(token);
        }
        foreach (var environment in await EnvironmentsAsync(gameId, token))
        {
            if (environment.Status is NetlabStatus.Destroyed or NetlabStatus.Destroying ||
                action == ActionRequestAction.Suspend && environment.Status != NetlabStatus.Running ||
                action == ActionRequestAction.Resume && environment.Status != NetlabStatus.Suspended) continue;
            await ActionAsync(environment.Id, environment.Revision, action, token);
        }
        return await SummaryAsync(gameId, token);
    }

    private async Task<PenetrationSceneSummary> SummaryAsync(int gameId, CancellationToken token)
    {
        var settings = await SettingsAsync(gameId, token);
        var assigned = await context.PenetrationTeamRuntimeBindings.AsNoTracking().Where(x => x.GameId == gameId)
            .Select(x => x.EnvironmentId).ToArrayAsync(token);
        var environments = (await EnvironmentsAsync(gameId, token)).Where(x => assigned.Contains(x.Id)).ToArray();
        int Count(params NetlabStatus[] states) => environments.Count(x => states.Contains(x.Status));
        var total = await context.Participations.CountAsync(x => x.GameId == gameId && x.Status == ParticipationStatus.Accepted, token);
        var counts = new PenetrationSceneCounts(total, Math.Max(0, total - environments.Length),
            Count(NetlabStatus.Deploying, NetlabStatus.Changing), Count(NetlabStatus.Running),
            settings.AccessOpen ? Count(NetlabStatus.Running) : 0, Count(NetlabStatus.Failed),
            Count(NetlabStatus.Destroying), Count(NetlabStatus.Destroyed), Count(NetlabStatus.Suspended));
        var status = counts.Draining > 0 ? "draining" : counts.Provisioning > 0 ? "preparing" : counts.Failed > 0 ? "failed" :
            counts.Ready == total && total > 0 ? "ready" : environments.Length > 0 && counts.Destroyed == environments.Length ? "completed" :
            counts.Paused > 0 ? "paused" : "pending";
        return new(settings.BlueprintVersionId!, settings.BlueprintVersionId!, status, environments.Length > 0,
            settings.AccessOpen, counts.Draining > 0, counts.Paused > 0, counts, settings.CreatedAt, settings.UpdatedAt);
    }

    private async Task<PenetrationGameLabBinding> SettingsAsync(int gameId, CancellationToken token) =>
        await context.PenetrationGameLabBindings.SingleOrDefaultAsync(x => x.GameId == gameId, token)
            ?? throw Conflict("请先选择比赛场景。");
    private async Task<PenetrationTeamRuntimeBinding> RuntimeAsync(int gameId, int teamId, CancellationToken token) =>
        await context.PenetrationTeamRuntimeBindings.SingleOrDefaultAsync(x => x.GameId == gameId && x.TeamId == teamId && x.EnvironmentId != null, token)
            ?? throw Conflict("本队环境尚未创建。");
    private Task<EnvironmentState> StateAsync(string id, CancellationToken token) =>
        netlab.GetAsync<EnvironmentState>($"environments/{Uri.EscapeDataString(id)}/state", token);
    private async Task<RequestScope> ManagerScopeAsync(Guid id, CancellationToken token) =>
        (await access.AuthorizeAsync(await context.Users.SingleAsync(x => x.Id == id, token), null, token)).Scope;
    private string GameProject(int gameId) => $"{connection.ProjectId}/games/{gameId}";
    private Task<IReadOnlyList<EnvironmentSummary>> EnvironmentsAsync(int gameId, CancellationToken token) =>
        netlab.ListAsync<EnvironmentSummary>("environments", x => x.Id, token, new RequestScope { Name = "比赛环境",
            Grants = [new ScopeGrant { ScopeKind = ScopeGrantScopeKind.Project, ScopeId = GameProject(gameId), Permissions = [Permission.Read] }] });
    private Task<Operation> ActionAsync(string id, int revision, ActionRequestAction action, CancellationToken token) =>
        netlab.SendAsync<Operation>(HttpMethod.Post, $"environments/{Uri.EscapeDataString(id)}/actions",
            new ActionRequest { Action = action, ExpectedRevision = revision, ClientRequestId = $"{id}:{action}:{revision}" }, token);
    private async Task RequireEmptyAsync(int gameId, CancellationToken token)
    {
        if ((await EnvironmentsAsync(gameId, token)).Any(x => x.Status != NetlabStatus.Destroyed))
            throw Conflict("请先清理已部署环境，再调整比赛场景。");
    }
    private static TeamLabApiContractException Conflict(string message) => new("scene_conflict", message, 409);
    public static TeamLabRuntimeStatus Status(string state) => state switch
    {
        "running" => TeamLabRuntimeStatus.Running, "stopped" => TeamLabRuntimeStatus.Stopped,
        "suspended" => TeamLabRuntimeStatus.Paused, "destroying" => TeamLabRuntimeStatus.Destroying,
        "destroyed" => TeamLabRuntimeStatus.Destroyed, "failed" or "unknown" => TeamLabRuntimeStatus.Failed,
        "draft" => TeamLabRuntimeStatus.Pending, _ => TeamLabRuntimeStatus.Deploying
    };
}
