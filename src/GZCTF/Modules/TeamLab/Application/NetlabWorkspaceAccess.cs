using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record NetlabWorkspaceContext(string BasePath, string ProjectId, string? EnvironmentId, RequestScope Scope);

public sealed class NetlabWorkspaceAccess(AppDbContext db, NetlabConnection connection, IParticipationRepository participations,
    IServiceScopeFactory scopes)
{
    public async Task<NetlabWorkspaceContext> AuthorizeAsync(UserInfo user, int? gameId, CancellationToken token)
    {
        if (user.DeletedAtUtc is not null || user.LockoutEnd > DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("account_unavailable", "当前账号已停用。", 403);
        if (gameId is { } id) return await GameAsync(user, id, token);
        if (user.Role < Role.Teacher) throw new TeamLabApiContractException("workspace_forbidden", "当前账号没有场景管理权限。", 403);
        var project = $"{connection.ProjectId}/users/{user.Id:D}";
        var scope = new RequestScope { Name = user.UserName!, Administrator = user.Role >= Role.Admin, Grants = [] };
        scope.Grants.Add(Grant(ScopeGrantScopeKind.Project, project, Enum.GetValues<Permission>()));
        var games = await db.Games.AsNoTracking().Where(game => game.OwnerId == user.Id).OrderBy(game => game.Id).Select(game => game.Id).ToArrayAsync(token);
        scope.Grants.AddRange(games.Select(game => Grant(ScopeGrantScopeKind.Project, $"{connection.ProjectId}/games/{game}", Enum.GetValues<Permission>())));
        return new("/api/teamlab/netlab/admin", project, null, scope);
    }

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
        var granted = NetlabClient.Serialize(expected.Scope);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(token))
        {
            await using var scope = scopes.CreateAsyncScope();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>().FindByIdAsync(initialUser.Id.ToString())
                ?? throw new TeamLabApiContractException("account_unavailable", "当前账号已停用。", 403);
            if (user.SecurityStamp != initialUser.SecurityStamp)
                throw new TeamLabApiContractException("session_expired", "登录状态已更新，请重新登录。", 401);
            var current = await scope.ServiceProvider.GetRequiredService<NetlabWorkspaceAccess>().AuthorizeAsync(user, gameId, token);
            if (current.EnvironmentId != expected.EnvironmentId || NetlabClient.Serialize(current.Scope) != granted)
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
        var scope = new RequestScope
        {
            Name = user.UserName!, Grants = [Grant(ScopeGrantScopeKind.Environment, binding.EnvironmentId,
                [Permission.Read, Permission.Session, Permission.Observe, Permission.Access])]
        };
        return new($"/api/teamlab/netlab/game/{gameId}", $"{connection.ProjectId}/games/{gameId}", binding.EnvironmentId, scope);
    }

    private static ScopeGrant Grant(ScopeGrantScopeKind kind, string id, IEnumerable<Permission> permissions) =>
        new() { ScopeKind = kind, ScopeId = id, Permissions = permissions.ToList() };
}
