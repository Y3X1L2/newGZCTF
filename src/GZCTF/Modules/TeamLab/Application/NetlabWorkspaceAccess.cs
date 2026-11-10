using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record NetlabWorkspaceContext(string BasePath, string ProjectId, string? EnvironmentId, RequestScope Scope);

public sealed class NetlabWorkspaceAccess(AppDbContext db, NetlabConnection connection, IParticipationRepository participations)
{
    public async Task<NetlabWorkspaceContext> AuthorizeAsync(UserInfo user, int? gameId, CancellationToken token)
    {
        if (gameId is { } id) return await GameAsync(user, id, token);
        if (user.Role < Role.Teacher) throw new TeamLabApiContractException("workspace_forbidden", "当前账号没有场景管理权限。", 403);
        var project = $"{connection.ProjectId}/users/{user.Id:D}";
        var scope = new RequestScope { Name = user.UserName!, Administrator = user.Role >= Role.Admin, Grants = [] };
        scope.Grants.Add(Grant(ScopeGrantScopeKind.Project, project, Enum.GetValues<Permission>()));
        var games = await db.Games.AsNoTracking().Where(game => game.OwnerId == user.Id).Select(game => game.Id).ToArrayAsync(token);
        scope.Grants.AddRange(games.Select(game => Grant(ScopeGrantScopeKind.Project, $"{connection.ProjectId}/games/{game}", Enum.GetValues<Permission>())));
        return new("/api/teamlab/netlab/admin", project, null, scope);
    }

    private async Task<NetlabWorkspaceContext> GameAsync(UserInfo user, int gameId, CancellationToken token)
    {
        var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == gameId, token)
            ?? throw new TeamLabApiContractException("game_not_found", "比赛不存在。", 404);
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
