using System.Diagnostics.CodeAnalysis;
using GZCTF.Hubs.Clients;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;

namespace GZCTF.Hubs;

[ExcludeFromCodeCoverage]
public class UserHub : Hub<IUserClient>
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
        if (user is null)
            return;

        var participation = await participationRepository.GetParticipation(user.Id, gId);
        if (participation?.Status != ParticipationStatus.Accepted)
            return;

        await Groups.AddToGroupAsync(Context.ConnectionId, PenetrationTeamGroupName(gId, participation.TeamId));
    }
}
