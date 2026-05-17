using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

/// <summary>
/// Leaderboard APIs for scenario and IR challenge rankings.
/// Provides ranked standings with per-dimension score breakdown.
/// </summary>
[ApiController]
[Route("api/v1/scenarios")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class LeaderboardController : ControllerBase
{
    private readonly LeaderboardService _leaderboardService;
    private readonly ILogger<LeaderboardController> _logger;

    public LeaderboardController(
        LeaderboardService leaderboardService,
        ILogger<LeaderboardController> logger)
    {
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
