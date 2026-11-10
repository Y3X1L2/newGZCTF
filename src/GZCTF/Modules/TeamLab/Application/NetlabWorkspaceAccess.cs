using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record NetlabWorkspaceContext(string BasePath, string ProjectId, string? EnvironmentId,
    Guid UserId, string Name, bool Administrator, int[] Games)
{
    public string ExternalReference => $"user:{UserId:D}";
    public bool CanRead(string project, string id, string? reference) => project == ProjectId &&
        (EnvironmentId is not null ? id == EnvironmentId : Administrator || reference == ExternalReference ||
            Games.Any(game => reference?.StartsWith($"game:{game}/team:", StringComparison.Ordinal) == true));
}

public sealed class NetlabWorkspaceAccess(AppDbContext db, NetlabConnection connection, IParticipationRepository participations,
    IServiceScopeFactory scopes, NetlabClient netlab)
{
    public async Task<NetlabWorkspaceContext> AuthorizeAsync(UserInfo user, int? gameId, CancellationToken token)
    {
        if (user.DeletedAtUtc is not null || user.LockoutEnd > DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("account_unavailable", "当前账号已停用。", 403);
        if (gameId is { } id) return await GameAsync(user, id, token);
        if (user.Role < Role.Teacher) throw new TeamLabApiContractException("workspace_forbidden", "当前账号没有场景管理权限。", 403);
        var games = await db.Games.AsNoTracking().Where(game => game.OwnerId == user.Id)
            .OrderBy(game => game.Id).Select(game => game.Id).ToArrayAsync(token);
        return new("/api/teamlab/netlab/admin", connection.ProjectId, null, user.Id, user.UserName!, user.Role >= Role.Admin, games);
    }

    public async Task<IReadOnlyList<EnvironmentSummary>> EnvironmentsAsync(NetlabWorkspaceContext workspace, CancellationToken token) =>
        (await netlab.ListAsync<EnvironmentSummary>("environments", item => item.Id, token))
            .Where(item => workspace.CanRead(item.ProjectId, item.Id, item.ExternalReference)).ToArray();

    public async Task<Infrastructure.Netlab.Environment> EnvironmentAsync(NetlabWorkspaceContext workspace, string id, CancellationToken token)
    {
        if (workspace.EnvironmentId is { } assigned && assigned != id) throw Forbidden();
        var environment = await netlab.GetAsync<Infrastructure.Netlab.Environment>($"environments/{Uri.EscapeDataString(id)}", token);
        if (!workspace.CanRead(environment.ProjectId, environment.Id, environment.ExternalReference)) throw Forbidden();
        if (workspace.EnvironmentId is not null)
        {
            environment.Permissions = Limit(environment.Permissions);
            if (environment.AssetPermissions is not null)
                foreach (var key in environment.AssetPermissions.Keys.ToArray())
                    environment.AssetPermissions[key] = Limit(environment.AssetPermissions[key]);
            foreach (var spec in new[] { environment.Spec, environment.AppliedSpec, environment.Draft?.Spec })
                if (spec is not null)
                    foreach (var asset in spec.Assets) { asset.Parameters = null; asset.Guest = null; }
        }
        return environment;
    }

    public async Task<Blueprint> BlueprintAsync(NetlabWorkspaceContext workspace, string id, CancellationToken token)
    {
        if (workspace.EnvironmentId is not null) throw Forbidden();
        var blueprint = await netlab.GetAsync<Blueprint>($"blueprints/{Uri.EscapeDataString(id)}", token);
        if (blueprint.ProjectId != workspace.ProjectId) throw Forbidden();
        if (!workspace.Administrator)
        {
            var version = await netlab.GetAsync<BlueprintVersion>($"blueprint-versions/{Uri.EscapeDataString(blueprint.LatestVersionId!)}", token);
            await EnvironmentAsync(workspace, version.SourceEnvironmentId!, token);
        }
        return blueprint;
    }

    public async Task<IReadOnlyList<Blueprint>> BlueprintsAsync(NetlabWorkspaceContext workspace, CancellationToken token)
    {
        var blueprints = (await netlab.ListAsync<Blueprint>("blueprints", item => item.Id, token))
            .Where(item => item.ProjectId == workspace.ProjectId).ToArray();
        if (workspace.Administrator) return blueprints;
        List<Blueprint> result = [];
        foreach (var blueprint in blueprints)
        {
            var version = await netlab.GetAsync<BlueprintVersion>($"blueprint-versions/{Uri.EscapeDataString(blueprint.LatestVersionId!)}", token);
            var source = await netlab.GetAsync<Infrastructure.Netlab.Environment>($"environments/{Uri.EscapeDataString(version.SourceEnvironmentId!)}", token);
            if (workspace.CanRead(source.ProjectId, source.Id, source.ExternalReference)) result.Add(blueprint);
        }
        return result;
    }

    public static List<Permission> Limit(IEnumerable<Permission>? permissions) =>
        (permissions ?? []).Where(permission => permission is Permission.Read or Permission.Session or Permission.Observe or Permission.Access).ToList();

    public static TeamLabApiContractException Forbidden() => new("workspace_forbidden", "当前账号无权操作此组网资源。", 403);

    public async Task RunSessionAsync(UserInfo user, int? gameId, NetlabWorkspaceContext workspace,
        Func<CancellationToken, Task> forward, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var forwarding = forward(lifetime.Token);
        var authorization = WatchAccessAsync(user, gameId, workspace, lifetime.Token);
        try { await await Task.WhenAny(forwarding, authorization); }
        finally
        {
            await lifetime.CancelAsync();
            try { await Task.WhenAll(forwarding, authorization); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }

    private async Task WatchAccessAsync(UserInfo initialUser, int? gameId, NetlabWorkspaceContext expected, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(token))
        {
            await using var scope = scopes.CreateAsyncScope();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>().FindByIdAsync(initialUser.Id.ToString())
                ?? throw new TeamLabApiContractException("account_unavailable", "当前账号已停用。", 403);
            if (user.SecurityStamp != initialUser.SecurityStamp)
                throw new TeamLabApiContractException("session_expired", "登录状态已更新，请重新登录。", 401);
            var current = await scope.ServiceProvider.GetRequiredService<NetlabWorkspaceAccess>().AuthorizeAsync(user, gameId, token);
            if (current.EnvironmentId != expected.EnvironmentId || current.Administrator != expected.Administrator || !current.Games.SequenceEqual(expected.Games))
                throw new TeamLabApiContractException("access_changed", "访问权限已更新，请重新打开工作区。", 403);
        }
    }

    private async Task<NetlabWorkspaceContext> GameAsync(UserInfo user, int gameId, CancellationToken token)
    {
        var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == gameId, token)
            ?? throw new TeamLabApiContractException("game_not_found", "比赛不存在。", 404);
        if (game.GameType is not GameType.Penetration and not GameType.Mixed)
            throw new TeamLabApiContractException("scene_unavailable", "当前比赛未启用组网场景。", 404);
        var participation = await participations.GetParticipation(user.Id, gameId, token);
        var binding = participation is null ? null : await db.PenetrationTeamRuntimeBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.TeamId == participation.TeamId, token);
        if (participation?.Status != ParticipationStatus.Accepted || binding?.EnvironmentId is null)
            throw new TeamLabApiContractException("environment_not_assigned", "本队尚未分配环境。", 403);
        if (DateTimeOffset.UtcNow < game.StartTimeUtc || (!game.PracticeMode && DateTimeOffset.UtcNow > game.EndTimeUtc))
            throw new TeamLabApiContractException("game_not_open", "当前比赛未开放环境。", 403);
        if (!await db.PenetrationGameLabBindings.AnyAsync(item => item.GameId == gameId && item.AccessOpen, token))
            throw new TeamLabApiContractException("access_not_open", "环境尚未开放。", 403);
        return new($"/api/teamlab/netlab/game/{gameId}", connection.ProjectId, binding.EnvironmentId,
            user.Id, user.UserName!, false, []);
    }
}
