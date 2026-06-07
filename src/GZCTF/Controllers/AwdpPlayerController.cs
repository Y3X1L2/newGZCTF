using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Cache;
using GZCTF.Services.Config;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// AWDP player APIs
/// </summary>
[RequireUser]
[ApiController]
[Route("api/awdp")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class AwdpPlayerController(
    AppDbContext context,
    UserManager<UserInfo> userManager,
    IGameRepository gameRepository,
    IParticipationRepository participationRepository,
    IAwdpRepository awdpRepository,
    IGameEventRepository eventRepository,
    AwdpRoundService roundService,
    AwdpInstanceService instanceService,
    AwdpPatchService patchService,
    AwdpScoreService scoreService,
    CacheHelper cacheHelper,
    IConfigService configService) : ControllerBase
{
    [HttpGet("Games/{gameId:int}/Status")]
    [ProducesResponseType(typeof(AwdpGameStatusModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus([FromRoute] int gameId, CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, denyAfterEnded: false, token);
        if (ctx.Result is not null)
            return ctx.Result;

        return Ok(await roundService.GetStatus(gameId, token));
    }

    [HttpGet("Games/{gameId:int}/Instances")]
    [ProducesResponseType(typeof(AwdpTeamServiceStatus[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInstances([FromRoute] int gameId, CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, denyAfterEnded: false, token);
        if (ctx.Result is not null)
            return ctx.Result;

        return Ok(await BuildTeamStatuses(gameId, ctx.Participation!.TeamId, token));
    }

    [HttpPost("Games/{gameId:int}/Flags")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]
    [ProducesResponseType(typeof(AwdpSubmitResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitFlag([FromRoute] int gameId, [FromBody] AwdpSubmitModel model,
        CancellationToken token)
    {
        var answer = configService.DecryptApiData(model.Flag)?.Trim();
        if (string.IsNullOrWhiteSpace(answer))
            return BadRequest(new RequestResponse("Flag is required."));

        if (answer.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse("Flag is too long."));

        var ctx = await GetContextInfo(gameId, token: token);
        if (ctx.Result is not null)
            return ctx.Result;

        var round = await awdpRepository.GetCurrentRound(gameId, token);
        if (round is null)
            return BadRequest(new RequestResponse("No active AWDP round."));

        if (round.Status != AwdpRoundStatus.AttackPhase)
            return BadRequest(new RequestResponse("AWDP flag submission is only allowed in attack phase."));

        var flag = await awdpRepository.GetFlagByValue(answer, token);
        if (flag is null || flag.Service.GameId != gameId || flag.RoundId != round.Id)
            return BadRequest(new RequestResponse("Invalid AWDP flag."));

        var teamId = ctx.Participation!.TeamId;
        if (flag.TeamId != teamId)
            return BadRequest(new RequestResponse("This AWDP flag does not belong to your team instance."));

        if (flag.IsSubmitted)
            return BadRequest(new RequestResponse("This AWDP flag has already been submitted."));

        var submittedCount = (await awdpRepository.GetFlagsByRound(round.Id, token))
            .Count(f => f.ServiceId == flag.ServiceId && f.SubmittedByTeamId == teamId);
        if (submittedCount >= flag.Service.MaxAttackPerRound)
            return BadRequest(new RequestResponse("The attack limit for this service and round has been reached."));

        var updated = await awdpRepository.UpdateFlagSubmitted(flag.Id, teamId, ctx.User!.Id, token);
        if (!updated)
            return StatusCode(StatusCodes.Status409Conflict,
                new RequestResponse("This AWDP flag has already been submitted.", StatusCodes.Status409Conflict));

        await eventRepository.AddEvent(new GameEvent
        {
            GameId = gameId,
            TeamId = teamId,
            UserId = ctx.User!.Id,
            Type = EventType.AwdpFlagSubmit,
            Values =
            [
                flag.Service.Name,
                round.RoundNumber.ToString(),
                flag.Service.AttackPoints.ToString()
            ]
        }, token);

        await cacheHelper.FlushScoreboardCache(gameId, token);

        return Ok(new AwdpSubmitResultModel
        {
            Accepted = true,
            Points = flag.Service.AttackPoints,
            RoundNumber = round.RoundNumber,
            ServiceId = flag.ServiceId,
            ServiceName = flag.Service.Name,
            Message = "AWDP flag accepted."
        });
    }

    [HttpPost("Games/{gameId:int}/Patches")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(typeof(AwdpPatchSubmissionViewModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitPatch([FromRoute] int gameId, [FromForm] AwdpPatchSubmitModel model,
        CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, token: token);
        if (ctx.Result is not null)
            return ctx.Result;

        var result = await patchService.SubmitPatch(gameId, ctx.Participation!.TeamId, model.ServiceId, model.File,
            token);
        if (result.Error is not null)
            return BadRequest(new RequestResponse(result.Error));

        return Ok(ToPatchViewModel(result.Submission!));
    }

    [HttpPost("Instances/{instanceId:int}/Reset")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(typeof(AwdpInstanceActionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetInstance([FromRoute] int instanceId, CancellationToken token)
    {
        var validation = await ValidateOwnedInstance(instanceId, token);
        if (validation.Result is not null)
            return validation.Result;

        var result = await instanceService.ResetInstanceByPlayer(instanceId, validation.Participation!.TeamId, token);
        return Ok(new AwdpInstanceActionModel
        {
            InstanceId = instanceId,
            Success = result.Success,
            Message = result.Message
        });
    }

    [HttpPost("Instances/{instanceId:int}/Recover")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(typeof(AwdpInstanceActionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecoverInstance([FromRoute] int instanceId, CancellationToken token)
    {
        var validation = await ValidateOwnedInstance(instanceId, token);
        if (validation.Result is not null)
            return validation.Result;

        var result = await instanceService.RecoverInstanceByPlayer(instanceId, validation.Participation!.TeamId,
            token);
        return Ok(new AwdpInstanceActionModel
        {
            InstanceId = instanceId,
            Success = result.Success,
            Message = result.Message
        });
    }

    [HttpGet("Games/{gameId:int}/Scoreboard")]
    [ProducesResponseType(typeof(AwdpScoreboardItem[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScoreboard([FromRoute] int gameId, CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, denyAfterEnded: false, token);
        if (ctx.Result is not null)
            return ctx.Result;

        var scoreboard = await gameRepository.GetScoreboard(ctx.Game!, token);
        var ctfScores = scoreboard.Items.Values.ToDictionary(i => i.Id, i => i.CtfScore);

        return Ok(await scoreService.GetScoreboard(gameId, ctfScores, token));
    }

    [HttpGet("Games/{gameId:int}/AttackLogs")]
    [ProducesResponseType(typeof(ArrayResponse<AwdpAttackLogItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAttackLogs([FromRoute] int gameId,
        [FromQuery][Range(0, 100)] int count = 50, [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var ctx = await GetContextInfo(gameId, denyAfterEnded: false, token);
        if (ctx.Result is not null)
            return ctx.Result;

        var query = context.AwdpFlags.AsNoTracking()
            .Where(f => f.Round.GameId == gameId && f.IsSubmitted && f.SubmittedByTeamId != null);

        var total = await query.CountAsync(token);
        var logs = await query
            .OrderByDescending(f => f.FirstSubmittedAt)
            .Skip(Math.Max(0, skip))
            .Take(count <= 0 ? 50 : count)
            .Select(f => new AwdpAttackLogItem
            {
                Time = f.FirstSubmittedAt ?? DateTimeOffset.MinValue,
                AttackerTeam = f.SubmittedByTeam == null ? string.Empty : f.SubmittedByTeam.Name,
                VictimTeam = f.Team.Name,
                ServiceName = f.Service.Name,
                Points = f.Service.AttackPoints
            })
            .ToArrayAsync(token);

        return Ok(logs.ToResponse(total));
    }

    [HttpGet("Games/{gameId:int}/PatchStatus")]
    [ProducesResponseType(typeof(AwdpPatchStatusItem[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatchStatus([FromRoute] int gameId, CancellationToken token)
    {
        var ctx = await GetContextInfo(gameId, denyAfterEnded: false, token);
        if (ctx.Result is not null)
            return ctx.Result;

        var teamId = ctx.Participation!.TeamId;
        var services = await awdpRepository.GetServicesByGame(gameId, token);
        var round = await context.AwdpRounds.AsNoTracking()
            .Where(r => r.GameId == gameId)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(token);

        var flags = round is null ? Array.Empty<AwdpFlag>() : await awdpRepository.GetFlagsByRound(round.Id, token);
        var patches = round is null
            ? Array.Empty<AwdpPatchSubmission>()
            : await awdpRepository.GetPatchSubmissionsByRound(round.Id, token);

        return Ok(services.Select(service =>
        {
            var flag = flags.FirstOrDefault(f => f.ServiceId == service.Id && f.SubmittedByTeamId == teamId);
            var patch = patches
                .Where(p => p.ServiceId == service.Id && p.TeamId == teamId)
                .OrderByDescending(p => p.SubmittedAt)
                .FirstOrDefault();

            return new AwdpPatchStatusItem
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                AttackStatus = flag is { IsSubmitted: true }
                    ? AwdpChallengeStatus.Attacked
                    : AwdpChallengeStatus.Unattacked,
                DefenseStatus = ToDefenseStatus(patch?.FinalStatus),
                LastPatchResult = patch?.FinalStatus,
                LastPatchTime = patch?.SubmittedAt,
                Message = patch?.Message
            };
        }).ToArray());
    }

    async Task<ContextInfo> GetContextInfo(int gameId, bool denyAfterEnded = true,
        CancellationToken token = default)
    {
        ContextInfo res = new()
        {
            User = await userManager.GetUserAsync(User),
            Game = await gameRepository.GetGameById(gameId, token)
        };

        if (res.User is null)
            return res.WithResult(Unauthorized(new RequestResponse("Login required.",
                StatusCodes.Status401Unauthorized)));

        if (res.Game is null)
            return res.WithResult(NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound)));

        if (res.Game.GameType is not GameType.AWDP and not GameType.Mixed)
            return res.WithResult(BadRequest(new RequestResponse("The game is not an AWDP or mixed game.")));

        var part = await participationRepository.GetParticipation(res.User.Id, res.Game.Id, token);
        if (part is null)
            return res.WithResult(BadRequest(new RequestResponse("You have not participated in this game.")));

        res.Participation = part;

        if (part.Status != ParticipationStatus.Accepted)
            return res.WithResult(BadRequest(new RequestResponse("Your participation has not been accepted.")));

        if (DateTimeOffset.UtcNow < res.Game.StartTimeUtc)
            return res.WithResult(BadRequest(new RequestResponse("The game has not started.",
                ErrorCodes.GameNotStarted)));

        if (denyAfterEnded && !res.Game.PracticeMode && res.Game.EndTimeUtc < DateTimeOffset.UtcNow)
            return res.WithResult(BadRequest(new RequestResponse("The game has ended.", ErrorCodes.GameEnded)));

        return res;
    }

    async Task<(Participation? Participation, IActionResult? Result)> ValidateOwnedInstance(int instanceId,
        CancellationToken token)
    {
        var instance = await awdpRepository.GetInstance(instanceId, token);
        if (instance is null)
            return (null, NotFound(new RequestResponse("AWDP instance not found.", StatusCodes.Status404NotFound)));

        var ctx = await GetContextInfo(instance.Service.GameId, token: token);
        if (ctx.Result is not null)
            return (ctx.Participation, ctx.Result);

        if (instance.TeamId != ctx.Participation!.TeamId)
            return (ctx.Participation, BadRequest(new RequestResponse("You cannot operate another team's instance.")));

        return (ctx.Participation, null);
    }

    async Task<AwdpTeamServiceStatus[]> BuildTeamStatuses(int gameId, int teamId, CancellationToken token)
    {
        var services = await awdpRepository.GetServicesByGame(gameId, token);
        var instances = await awdpRepository.GetInstancesByGame(gameId, token);
        var round = await awdpRepository.GetCurrentRound(gameId, token);
        var checkerTasks = round is null
            ? Array.Empty<AwdpCheckerTask>()
            : await awdpRepository.GetCheckerTasksByRound(round.Id, token);
        var resets = await awdpRepository.GetResetRecordsByGame(gameId, token);
        var recoveries = await awdpRepository.GetRecoveryRecordsByGame(gameId, token);

        return services.SelectMany(service => instances
            .Where(i => i.ServiceId == service.Id && i.TeamId == teamId)
            .Select(i => new AwdpTeamServiceStatus
            {
                InstanceId = i.Id,
                ServiceId = service.Id,
                ServiceName = service.Name,
                TeamId = i.TeamId,
                TeamName = i.Team.Name,
                IpAddress = i.Container?.PublicIP ?? i.Container?.IP,
                Port = i.Container?.PublicPort ?? i.Container?.Port,
                LastCheckerStatus = checkerTasks
                    .FirstOrDefault(t => t.ServiceId == service.Id && t.TeamId == i.TeamId)?.Status,
                IsRunning = i.IsRunning && i.Container?.Status == ContainerStatus.Running,
                RemainingResetCount = Math.Max(0,
                    service.MaxResetCount - resets.Count(r =>
                        r.ServiceId == service.Id && r.TeamId == i.TeamId &&
                        r.ResetType == AwdpResetType.Player)),
                RemainingRecoveryCount = Math.Max(0,
                    service.MaxRecoveryCount - recoveries.Count(r => r.ServiceId == service.Id && r.TeamId == i.TeamId))
            })).ToArray();
    }

    static AwdpChallengeStatus ToDefenseStatus(AwdpPatchStatus? status) =>
        status switch
        {
            null or AwdpPatchStatus.Pending => AwdpChallengeStatus.Undefended,
            AwdpPatchStatus.ExpFailed => AwdpChallengeStatus.Defended,
            AwdpPatchStatus.CheckerFailed => AwdpChallengeStatus.DefenseAbnormal,
            AwdpPatchStatus.ExpSucceeded or AwdpPatchStatus.Timeout or AwdpPatchStatus.Unsupported =>
                AwdpChallengeStatus.DefenseFailed,
            _ => AwdpChallengeStatus.Undefended
        };

    static AwdpPatchSubmissionViewModel ToPatchViewModel(AwdpPatchSubmission patch) => new()
    {
        Id = patch.Id,
        RoundId = patch.RoundId,
        RoundNumber = patch.Round.RoundNumber,
        ServiceId = patch.ServiceId,
        ServiceName = patch.Service.Name,
        TeamId = patch.TeamId,
        TeamName = patch.Team.Name,
        PatchFileHash = patch.PatchFileHash,
        SubmittedAt = patch.SubmittedAt,
        CheckerResult = patch.CheckerResult,
        ExpResult = patch.ExpResult,
        FinalStatus = patch.FinalStatus,
        Message = patch.Message
    };

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
