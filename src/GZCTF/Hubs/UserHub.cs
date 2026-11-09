using System.Diagnostics.CodeAnalysis;
using GZCTF.Hubs.Clients;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Hubs;

[ExcludeFromCodeCoverage]
public class UserHub(UserHubAccountConnections connections) : Hub<IUserClient>
{
    public static string PenetrationTeamGroupName(int gameId, int teamId) =>
        $"Game_{gameId}_PentestTeam_{teamId}";

    public override async Task OnConnectedAsync()
    {
        var context = Context.GetHttpContext();

        if (context is null
            || !context.Request.Query.TryGetValue("game", out var gameId)
            || !int.TryParse(gameId, out var gId))
        {
            Context.Abort();
            return;
        }

        var gameRepository = context.RequestServices.GetRequiredService<IGameRepository>();
        if (!await gameRepository.HasGameAsync(gId))
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"Game_{gId}");

        if (Context.User?.Identity?.IsAuthenticated != true)
            return;

        var userManager = context.RequestServices.GetRequiredService<UserManager<UserInfo>>();
        var participationRepository = context.RequestServices.GetRequiredService<IParticipationRepository>();
        var user = await userManager.GetUserAsync(Context.User);
        if (user is null || user.DeletedAtUtc is not null || user.Role == Role.Banned)
        {
            Context.Abort();
            return;
        }

        connections.Register(user.Id, Context.ConnectionId, Context.Abort);
        // Close the race between the initial identity read and registration.
        if (!await userManager.Users.AsNoTracking().AnyAsync(u => u.Id == user.Id &&
                u.DeletedAtUtc == null && u.Role != Role.Banned))
        {
            connections.Remove(Context.ConnectionId);
            Context.Abort();
            return;
        }

        var participation = await participationRepository.GetParticipation(user.Id, gId);
        if (participation?.Status != ParticipationStatus.Accepted)
            return;

        await Groups.AddToGroupAsync(Context.ConnectionId, PenetrationTeamGroupName(gId, participation.TeamId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
