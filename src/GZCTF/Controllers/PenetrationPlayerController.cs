using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Config;
using GZCTF.Services.TeamLab;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/pentest")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class PenetrationPlayerController(
    UserManager<UserInfo> userManager,
    IGameRepository gameRepository,
    IParticipationRepository participationRepository,
    PenetrationService penetrationService,
    AppDbContext context,
    TeamLabWireGuardService teamLabWireGuardService,
    IConfigService configService) : ControllerBase
{
    [RequireUser]
    [HttpGet("games/{gameId:int}/workspace")]
    [ProducesResponseType(typeof(PenetrationWorkspaceModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspace([FromRoute] int gameId, CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, token: token);
        if (ctx.Result is not null)
            return ctx.Result;

        var workspace = await penetrationService.GetWorkspace(gameId, ctx.Participation!.TeamId, token);
        return workspace is null
            ? NotFound(new RequestResponse("Penetration environment is not deployed.", StatusCodes.Status404NotFound))
            : Ok(workspace);
    }

    [RequireUser]
    [HttpGet("games/{gameId:int}/teamlab/vpn-config")]
    [ProducesResponseType(typeof(TeamLabClientConfigModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamLabVpnConfig([FromRoute] int gameId, CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, token: token);
        if (ctx.Result is not null)
            return ctx.Result;

        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Include(r => r.Team)
            .Include(r => r.VpnPeers)
            .Include(r => r.PublicUdpMapping)
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == ctx.Participation!.TeamId, token);

        if (runtime is null)
            return NotFound(new RequestResponse("TeamLab VPN environment is not deployed.", StatusCodes.Status404NotFound));

        var model = teamLabWireGuardService.BuildClientConfigModel(runtime);
        return model is null
            ? NotFound(new RequestResponse("TeamLab VPN configuration is not ready.", StatusCodes.Status404NotFound))
            : Ok(model);
    }

    [RequireUser]
    [HttpPost("games/{gameId:int}/submit")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]
    [ProducesResponseType(typeof(PenetrationSubmitResultModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromRoute] int gameId, [FromBody] PenetrationSubmitModel model,
        CancellationToken token)
    {
        var flag = configService.DecryptApiData(model.Flag)?.Trim() ?? model.Flag.Trim();
        if (string.IsNullOrWhiteSpace(flag))
            return BadRequest(new RequestResponse("Flag is required."));

        if (flag.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse("Flag is too long."));

        var ctx = await GetContextInfo(gameId, token: token);
        if (ctx.Result is not null)
            return ctx.Result;

        var result = await penetrationService.Submit(gameId, ctx.Participation!.TeamId, ctx.Participation.Id,
            ctx.User!.Id, new PenetrationSubmitModel { ScoreItemId = model.ScoreItemId, Flag = flag }, token);
        return Ok(result);
    }

    [RequireUser]
    [HttpPost("games/{gameId:int}/reset")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reset([FromRoute] int gameId, CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, token: token);
        if (ctx.Result is not null)
            return ctx.Result;

        var result = await penetrationService.RebuildTeam(gameId, ctx.Participation!.TeamId, false, ctx.User!.Id, token);
        return result.Success ? Ok(new RequestResponse(result.Message, StatusCodes.Status200OK))
            : BadRequest(new RequestResponse(result.Message));
    }

    [RequireUser]
    [HttpGet("games/{gameId:int}/scoreboard")]
    [ProducesResponseType(typeof(PenetrationScoreboardItemModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScoreboard([FromRoute] int gameId, CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, denyAfterEnded: false, allowTeacherMonitor: true, token: token);
        if (ctx.Result is not null)
            return ctx.Result;

        return Ok(await penetrationService.GetScoreboard(gameId, token));
    }

    async Task<ContextInfo> GetContextInfo(int gameId, bool denyAfterEnded = true,
        bool requireParticipation = true, bool allowTeacherMonitor = false, CancellationToken token = default)
    {
        ContextInfo res = new()
        {
            User = await userManager.GetUserAsync(User),
            Game = await gameRepository.GetGameById(gameId, token)
        };

        if (res.Game is null)
            return res.WithResult(NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound)));

        if (res.Game.GameType is not GameType.Penetration and not GameType.Mixed)
            return res.WithResult(BadRequest(new RequestResponse("The game is not a penetration or mixed game.")));

        if (DateTimeOffset.UtcNow < res.Game.StartTimeUtc)
            return res.WithResult(BadRequest(new RequestResponse("The game has not started.",
                ErrorCodes.GameNotStarted)));

        if (denyAfterEnded && !res.Game.PracticeMode && res.Game.EndTimeUtc < DateTimeOffset.UtcNow)
            return res.WithResult(BadRequest(new RequestResponse("The game has ended.", ErrorCodes.GameEnded)));

        if (!requireParticipation && res.User is null)
            return res.WithResult(Unauthorized(new RequestResponse("Login required.",
                StatusCodes.Status401Unauthorized)));

        if (!requireParticipation && (!allowTeacherMonitor || res.User!.Role >= Role.Teacher))
            return res;

        if (res.User is null)
            return res.WithResult(Unauthorized(new RequestResponse("Login required.",
                StatusCodes.Status401Unauthorized)));

        var part = await participationRepository.GetParticipation(res.User.Id, res.Game.Id, token);
        if (part is null)
            return res.WithResult(StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse("You have not participated in this game.", StatusCodes.Status403Forbidden)));

        res.Participation = part;

        if (part.Status != ParticipationStatus.Accepted)
            return res.WithResult(StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse("Your participation has not been accepted.", StatusCodes.Status403Forbidden)));

        return res;
    }

    sealed class ContextInfo
    {
        public Game? Game;
        public Participation? Participation;
        public UserInfo? User;
        public IActionResult? Result;

        public ContextInfo WithResult(IActionResult result)
        {
            Result = result;
            return this;
        }
    }
}
