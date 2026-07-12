using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Modules.Penetration.Application;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Config;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/pentest")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class PenetrationPlayerController(
    UserManager<UserInfo> users,
    IGameRepository games,
    IParticipationRepository participations,
    AppDbContext context,
    PenetrationWorkspaceService workspaces,
    PenetrationObjectiveService objectives,
    PenetrationTeamLabAdapter adapter,
    TeamLabAccessGrantService access,
    IConfigService config) : ControllerBase
{
    [RequireUser]
    [HttpGet("games/{gameId:int}/workspace")]
    public async Task<IActionResult> GetWorkspace(int gameId, CancellationToken cancellationToken)
    {
        var actor = await GetContextAsync(gameId, true, cancellationToken);
        if (actor.Error is not null) return actor.Error;
        var workspace = await workspaces.GetAsync(gameId, actor.Participation!.TeamId, cancellationToken);
        return workspace is null
            ? NotFound(new RequestResponse("Penetration environment is not deployed."))
            : Ok(workspace);
    }

    [RequireUser]
    [HttpPost("games/{gameId:int}/access-grants")]
    public async Task<IActionResult> CreateAccessGrant(int gameId, CancellationToken cancellationToken)
    {
        var actor = await GetContextAsync(gameId, true, cancellationToken);
        if (actor.Error is not null) return actor.Error;
        var runtimeId = await ResolveRuntimePublicIdAsync(gameId, actor.Participation!.TeamId, cancellationToken);
        var grant = await access.CreateAsync(runtimeId, cancellationToken);
        var downloadToken = grant.ConfigurationDownloadUrl?.Split("token=", 2, StringSplitOptions.None).ElementAtOrDefault(1);
        var playerDownloadUrl = string.IsNullOrWhiteSpace(downloadToken)
            ? null
            : $"/api/pentest/games/{gameId}/access-grants/{grant.Id:D}/download?token={Uri.EscapeDataString(downloadToken)}";
        return Created(string.Empty, grant with { ConfigurationDownloadUrl = playerDownloadUrl });
    }

    [RequireUser]
    [HttpGet("games/{gameId:int}/access-grants/{grantId:guid}/download")]
    public async Task<IActionResult> DownloadAccessGrant(
        int gameId,
        Guid grantId,
        [FromQuery] string token,
        CancellationToken cancellationToken)
    {
        var actor = await GetContextAsync(gameId, true, cancellationToken);
        if (actor.Error is not null) return actor.Error;
        var runtimeId = await ResolveRuntimePublicIdAsync(gameId, actor.Participation!.TeamId, cancellationToken);
        var download = await access.ConsumeConfigurationAsync(runtimeId, grantId, token, cancellationToken);
        return File(System.Text.Encoding.UTF8.GetBytes(download.Configuration),
            "application/x-wireguard-profile", download.FileName);
    }

    [RequireUser]
    [HttpPost("games/{gameId:int}/submit")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]
    public async Task<IActionResult> Submit(
        int gameId,
        PenetrationSubmitModel model,
        CancellationToken cancellationToken)
    {
        var flag = config.DecryptApiData(model.Flag)?.Trim() ?? model.Flag.Trim();
        if (string.IsNullOrWhiteSpace(flag) || flag.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse("Flag is required and must satisfy the length limit."));
        var actor = await GetContextAsync(gameId, true, cancellationToken);
        if (actor.Error is not null) return actor.Error;
        return Ok(await objectives.SubmitAsync(
            gameId, actor.Participation!.TeamId, actor.Participation.Id, actor.User!.Id,
            model with { Flag = flag }, cancellationToken));
    }

    [RequireUser]
    [HttpPost("games/{gameId:int}/reset")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    public async Task<IActionResult> Reset(int gameId, CancellationToken cancellationToken)
    {
        var actor = await GetContextAsync(gameId, true, cancellationToken);
        if (actor.Error is not null) return actor.Error;
        var result = await adapter.ResetTeamAsync(
            gameId, actor.Participation!.TeamId, actor.User!.Id, false, cancellationToken);
        return Accepted(new { runtimeId = result.RuntimePublicId });
    }

    [RequireUser]
    [HttpGet("games/{gameId:int}/scoreboard")]
    public async Task<IActionResult> GetScoreboard(int gameId, CancellationToken cancellationToken)
    {
        var actor = await GetContextAsync(gameId, false, cancellationToken);
        return actor.Error ?? Ok(await objectives.GetScoreboardAsync(gameId, cancellationToken));
    }

    private async Task<Guid> ResolveRuntimePublicIdAsync(int gameId, int teamId, CancellationToken cancellationToken) =>
        await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.GameId == gameId && item.TeamId == teamId)
            .Select(item => context.TeamLabRuntimes.Where(runtime => runtime.Id == item.RuntimeId)
                .Select(runtime => runtime.PublicId).Single())
            .SingleAsync(cancellationToken);

    private async Task<PlayerContext> GetContextAsync(
        int gameId,
        bool denyAfterEnded,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        var game = await games.GetGameById(gameId, cancellationToken);
        if (game is null) return new(null, null, NotFound(new RequestResponse("Game not found.")));
        if (game.GameType is not GameType.Penetration and not GameType.Mixed)
            return new(null, null, BadRequest(new RequestResponse("The game has no penetration module.")));
        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return new(null, null, BadRequest(new RequestResponse("The game has not started.", ErrorCodes.GameNotStarted)));
        if (denyAfterEnded && !game.PracticeMode && game.EndTimeUtc < DateTimeOffset.UtcNow)
            return new(null, null, BadRequest(new RequestResponse("The game has ended.", ErrorCodes.GameEnded)));
        if (user is null) return new(null, null, Unauthorized(new RequestResponse("Login required.")));
        if (!denyAfterEnded && user.Role >= Role.Teacher) return new(user, null, null);
        var participation = await participations.GetParticipation(user.Id, gameId, cancellationToken);
        if (participation?.Status != ParticipationStatus.Accepted)
            return new(user, null, StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse("Accepted participation is required.")));
        return new(user, participation, null);
    }

    private sealed record PlayerContext(UserInfo? User, Participation? Participation, IActionResult? Error);
}
