using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// AWD Player APIs
/// </summary>
[RequireUser]
[ApiController]
[Route("api/awd")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class AwdPlayerController(
    AppDbContext context,
    IAwdRepository awdRepository,
    AwdScoreService scoreService,
    AwdRoundService roundService,
    IGameRepository gameRepository,
    IParticipationRepository participationRepository,
    UserManager<UserInfo> userManager,
    ILogger<AwdPlayerController> logger) : ControllerBase
{
    /// <summary>
    /// Get AWD game status for player
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Game status</response>
    /// <response code="404">Game not found</response>
    [HttpGet("games/{gameId:int}/status")]
    [ProducesResponseType(typeof(AwdGameStatusModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameStatus(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        var services = await awdRepository.GetServicesByGame(gameId, token);
        var currentRound = roundService.GetCurrentRound(gameId);
        var rounds = await awdRepository.GetRoundsByGame(gameId, token);
        var currentRoundInfo = rounds.FirstOrDefault(r => r.RoundNumber == currentRound);

        return Ok(new AwdGameStatusModel
        {
            GameId = gameId,
            CurrentRound = currentRound ?? 0,
            RoundStartTime = currentRoundInfo?.StartTime ?? DateTimeOffset.UtcNow,
            RoundDurationMinutes = services.FirstOrDefault()?.RoundDurationMinutes ?? 5,
            Status = currentRound.HasValue ? AwdRoundStatus.Running : AwdRoundStatus.Preparing
        });
    }

    /// <summary>
    /// Get player's AWD service instances
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Instance info list</response>
    /// <response code="404">Game not found</response>
    /// <response code="403">Not participating</response>
    [HttpGet("games/{gameId:int}/instances")]
    [ProducesResponseType(typeof(TeamServiceStatus[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyInstances(int gameId, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        var participation = await participationRepository.GetParticipation(user!.Id, gameId, token);
        if (participation is null)
            return StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse("您未参加该比赛", StatusCodes.Status403Forbidden));

        var instances = await awdRepository.GetInstancesByGame(gameId, token);
        var myInstances = instances.Where(i => i.TeamId == participation.TeamId).Select(i => new TeamServiceStatus
        {
            TeamId = i.TeamId,
            TeamName = i.Team?.Name ?? string.Empty,
            IpAddress = i.Container?.IP,
            Port = i.Container?.Port,
            IsRunning = i.IsRunning && i.Container?.Status == ContainerStatus.Running
        }).ToArray();

        return Ok(myInstances);
    }

    /// <summary>
    /// Submit an AWD flag
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Flag accepted</response>
    /// <response code="400">Invalid flag or game state</response>
    /// <response code="404">Game not found</response>
    /// <response code="403">Not participating</response>
    [HttpPost("games/{gameId:int}/submit")]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitFlag(int gameId, [FromBody] AwdSubmitModel model, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        if (game.GameType is not GameType.AWD and not GameType.Mixed)
            return BadRequest(new RequestResponse("该比赛不支持AWD模式"));

        // Check game time
        var now = DateTimeOffset.UtcNow;
        if (game.StartTimeUtc > now)
            return BadRequest(new RequestResponse("比赛尚未开始"));
        if (game.EndTimeUtc < now)
            return BadRequest(new RequestResponse("比赛已结束"));

        var participation = await participationRepository.GetParticipation(user!.Id, gameId, token);
        if (participation is null)
            return StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse("您未参加该比赛", StatusCodes.Status403Forbidden));

        // Find flag by value
        var flag = await awdRepository.GetFlagByValue(model.Flag, token);
        if (flag is null)
            return BadRequest(new RequestResponse("Flag无效"));

        if (flag.Round?.GameId != gameId)
            return BadRequest(new RequestResponse("Flag不属于当前比赛"));

        // Cannot attack own flag
        if (flag.TeamId == participation.TeamId)
            return BadRequest(new RequestResponse("不能提交自己队伍的Flag"));

        // Check duplicate submission by this team for this flag
        var alreadySubmitted = await context.Submissions.AnyAsync(
            s => s.TeamId == participation.TeamId
                 && s.ChallengeId == flag.ServiceId
                 && s.Answer == flag.FlagValue,
            token);

        if (alreadySubmitted)
            return BadRequest(new RequestResponse("已经提交过这个Flag"));

        var service = await awdRepository.GetService(flag.ServiceId, token);
        if (service is null)
            return BadRequest(new RequestResponse("对应服务不存在"));

        await using var transaction = await context.Database.BeginTransactionAsync(token);

        try
        {
            await scoreService.RecordFlagSubmission(gameId, participation.TeamId, flag, service, token);
            await transaction.CommitAsync(token);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(token);
            logger.LogError(ex, "Flag submission failed for game {GameId}, team {TeamId}", gameId, participation.TeamId);
            return BadRequest(new RequestResponse("提交失败，请稍后重试"));
        }

        logger.LogInformation("AWD flag submitted: game={GameId}, attacker={AttackerTeamId}, victim={VictimTeamId}, service={ServiceId}",
            gameId, participation.TeamId, flag.TeamId, flag.ServiceId);

        return Ok(new RequestResponse("Flag提交成功", StatusCodes.Status200OK));
    }

    /// <summary>
    /// Get AWD scoreboard
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Scoreboard</response>
    /// <response code="404">Game not found</response>
    [HttpGet("games/{gameId:int}/scoreboard")]
    [ProducesResponseType(typeof(AwdScoreboardItem[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScoreboard(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        var services = await awdRepository.GetServicesByGame(gameId, token);
        var participations = await participationRepository.GetParticipations(game, token);
        var acceptedParts = participations.Where(p => p.Status == ParticipationStatus.Accepted).ToList();

        var submissions = await context.Submissions
            .Where(s => s.GameId == gameId && s.SubmissionType == ScoringSubmissionType.Flag)
            .ToListAsync(token);

        var result = new List<AwdScoreboardItem>();
        int rank = 1;

        foreach (var part in acceptedParts.OrderByDescending(p => CalculateAwdScore(p.TeamId, services, submissions)))
        {
            var (attack, sla, lost) = CalculateAwdBreakdown(part.TeamId, services, submissions);
            result.Add(new AwdScoreboardItem
            {
                Rank = rank++,
                TeamId = part.TeamId,
                TeamName = part.Team?.Name ?? string.Empty,
                AwdScore = attack + sla - lost,
                AttackScore = attack,
                SlaScore = sla,
                DefenseLost = lost
            });
        }

        return Ok(result.ToArray());
    }

    /// <summary>
    /// Get AWD attack logs
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Attack logs</response>
    /// <response code="404">Game not found</response>
    [HttpGet("games/{gameId:int}/attack-logs")]
    [ProducesResponseType(typeof(AwdAttackLogItem[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttackLogs(int gameId, [FromQuery][Range(0, 100)] int count = 50,
        [FromQuery] int skip = 0, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        var events = await context.GameEvents
            .Where(e => e.GameId == gameId && e.Type == EventType.AwdFlagSubmit)
            .OrderByDescending(e => e.PublishTimeUtc)
            .Skip(skip).Take(count)
            .ToListAsync(token);

        var result = events.Select(e => new AwdAttackLogItem
        {
            Time = e.PublishTimeUtc,
            AttackerTeam = e.TeamName,
            VictimTeam = e.Values?.ElementAtOrDefault(1) ?? string.Empty,
            ServiceName = e.Values?.ElementAtOrDefault(2) ?? string.Empty,
            Points = int.TryParse(e.Values?.ElementAtOrDefault(0)?.Replace("+", "").Replace(" pts", ""), out var pts) ? pts : 0
        }).ToArray();

        return Ok(result);
    }

    private static int CalculateAwdScore(int teamId, AwdService[] services, List<Submission> submissions)
    {
        var (attack, sla, lost) = CalculateAwdBreakdown(teamId, services, submissions);
        return attack + sla - lost;
    }

    private static (int Attack, int Sla, int Lost) CalculateAwdBreakdown(int teamId, AwdService[] services,
        List<Submission> submissions)
    {
        int attack = submissions
            .Where(s => s.TeamId == teamId && s.Status == AnswerResult.Accepted)
            .Sum(s => s.Score);

        // For SLA and defense lost, we would need checker task data.
        // This is a simplified placeholder implementation.
        int sla = 0;
        int lost = 0;

        return (attack, sla, lost);
    }
}
