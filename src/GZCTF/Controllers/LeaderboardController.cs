using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// Leaderboard APIs for scenario and IR challenge rankings.
/// Provides ranked standings with per-dimension score breakdown.
/// </summary>
[ApiController]
[Route("api/v1/scenarios")]
[LegacyFeatureGone("独立 Scenario 排行榜模块已停用，请使用比赛排行榜。")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class LeaderboardController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<UserInfo> _userManager;
    private readonly LeaderboardService _leaderboardService;
    private readonly ILogger<LeaderboardController> _logger;

    public LeaderboardController(
        AppDbContext dbContext,
        UserManager<UserInfo> userManager,
        LeaderboardService leaderboardService,
        ILogger<LeaderboardController> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _leaderboardService = leaderboardService;
        _logger = logger;
    }

    /// <summary>
    /// Get the leaderboard for a scenario or IR challenge.
    /// Returns ranked entries with total score and per-dimension score breakdown.
    /// </summary>
    /// <param name="challengeId">The challenge ID (scenario or IR challenge)</param>
    /// <param name="token"></param>
    [HttpGet("{challengeId:int}/leaderboard")]
    [RequireUser]
    [ProducesResponseType(typeof(LeaderboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLeaderboard(
        [FromRoute] int challengeId,
        CancellationToken token)
    {
        var challenge = await _dbContext.GameChallenges
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == challengeId
                && (c.Type == ChallengeType.Scenario || c.Type == ChallengeType.IRChallenge), token);

        if (challenge is null || !challenge.IsEnabled)
            return NotFound(new RequestResponse("Challenge not found.", StatusCodes.Status404NotFound));

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(new RequestResponse("Login required.", StatusCodes.Status401Unauthorized));

        if (!await CanAccessGameAsync(user, challenge.GameId, token))
            return Forbid();

        var entries = await _leaderboardService.GetLeaderboardAsync(challengeId);

        _logger.LogDebug(
            "Leaderboard requested for Challenge {ChallengeId}, returned {Count} entries",
            challengeId, entries.Count);

        return Ok(new LeaderboardResponse
        {
            ChallengeId = challengeId,
            Entries = entries.Select(e => new LeaderboardEntryResponse
            {
                Rank = e.Rank,
                UserId = e.UserId,
                UserName = e.UserName,
                TotalScore = e.TotalScore,
                DetailScores = e.FlattenedDetailScores
            }).ToList(),
            GeneratedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task<bool> CanAccessGameAsync(UserInfo user, int gameId, CancellationToken token)
    {
        if (user.Role >= Role.Teacher)
            return true;

        return await _dbContext.Set<UserParticipation>()
            .AsNoTracking()
            .Include(up => up.Participation)
            .AnyAsync(up => up.UserId == user.Id
                && up.GameId == gameId
                && up.Participation.Status == ParticipationStatus.Accepted, token);
    }
}

/// <summary>
/// Response model for a leaderboard query.
/// </summary>
public class LeaderboardResponse
{
    public int ChallengeId { get; set; }
    public List<LeaderboardEntryResponse> Entries { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>
/// A single leaderboard entry with rank, score, and detail breakdown.
/// </summary>
public class LeaderboardEntryResponse
{
    public int Rank { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public Dictionary<string, int> DetailScores { get; set; } = [];
}
