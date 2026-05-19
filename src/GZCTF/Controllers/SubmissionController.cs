using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using GZCTF.Extensions;
using GZCTF.Hubs;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services;
using GZCTF.Services.Scoring;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// Multi-type submission management APIs.
/// Handles Flag, Writeup, IP, Credential, and Custom submission types
/// with auto-verification, manual review, and scoring integration.
/// </summary>
[ApiController]
[Route("api/v1/submissions")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class SubmissionController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<UserInfo> _userManager;
    private readonly ScoringService _scoringService;
    private readonly LeaderboardService _leaderboardService;
    private readonly IHubContext<ScenarioHub> _hubContext;
    private readonly ILogger<SubmissionController> _logger;
    private readonly GamePhaseService _phaseService;
    private readonly UnifiedScoringEngine _scoringEngine;

    public SubmissionController(
        AppDbContext context,
        UserManager<UserInfo> userManager,
        ScoringService scoringService,
        LeaderboardService leaderboardService,
        IHubContext<ScenarioHub> hubContext,
        ILogger<SubmissionController> logger,
        GamePhaseService phaseService,
        UnifiedScoringEngine scoringEngine)
    {
        _context = context;
        _userManager = userManager;
        _scoringService = scoringService;
        _leaderboardService = leaderboardService;
        _hubContext = hubContext;
        _logger = logger;
        _phaseService = phaseService;
        _scoringEngine = scoringEngine;
    }

    #region Submission CRUD

    /// <summary>
    /// Create a multi-type submission. Auto-verifies Flag (exact hash match) and IP (exact hash match).
    /// Writeup submissions are queued for manual review (status=Pending).
    /// Checks attempt limits against scoring rules.
    /// </summary>
    [HttpPost]
    [RequireUser]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]
    [ProducesResponseType(typeof(SubmissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSubmission(
        [FromBody] SubmissionCreateRequest request,
        CancellationToken token)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.Answer))
            return BadRequest(new RequestResponse("Answer is required."));

        if (request.Answer.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse("Answer exceeds maximum length."));

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(new RequestResponse("Login required."));

        var phaseCheck = await _phaseService.CheckAsync(request.GameId, PhaseRequiredType.CTF, token);
        if (phaseCheck != PhaseCheckResult.Allowed)
            return Forbid();

        // Find the scoring rule for this challenge and submission type
        var rule = await _context.ScoringRules
            .FirstOrDefaultAsync(r => r.ChallengeId == request.ChallengeId
                && r.SubmissionType == request.SubmissionType, token);

        if (rule is null)
            return BadRequest(new RequestResponse(
                "No scoring rule found for this challenge and submission type."));

        // Check attempt limits
        if (rule.MaxAttempts > 0)
        {
            var attemptCount = await _context.Submissions
                .CountAsync(s => s.ChallengeId == request.ChallengeId
                    && s.UserId == user.Id
                    && s.SubmissionType == request.SubmissionType, token);

            if (attemptCount >= rule.MaxAttempts)
                return BadRequest(new RequestResponse(
                    $"Maximum attempts ({rule.MaxAttempts}) reached for this submission type."));
        }

        // Delegate verification, scoring, and persistence to the unified engine
        // Engine handles: rule lookup, attempt limit check, verification, score decay, and DB save
        var (status, score) = await VerifySubmissionAsync(request, rule, user, token);

        // Engine already saved the Submission; retrieve it for the response
        var submission = await _context.Submissions
            .Where(s => s.ChallengeId == request.ChallengeId
                && s.UserId == user.Id
                && s.SubmissionType == request.SubmissionType)
            .OrderByDescending(s => s.SubmitTimeUtc)
            .FirstOrDefaultAsync(token);

        // T061: Push score and leaderboard updates via SignalR
        if (status == AnswerResult.Accepted)
        {
            await BroadcastScoreAndLeaderboardAsync(request.ChallengeId, user.Id);
        }

        if (submission is null)
            return Ok(new { status, score });

        return Ok(SubmissionResponse.FromSubmission(submission));
    }

    /// <summary>
    /// Query submissions with optional filters.
    /// </summary>
    [HttpGet]
    [RequireUser]
    [ProducesResponseType(typeof(ArrayResponse<SubmissionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> QuerySubmissions(
        [FromQuery] int? challengeId,
        [FromQuery] Guid? userId,
        [FromQuery] ScoringSubmissionType? submissionType,
        [FromQuery] int count = 50,
        [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(new RequestResponse("Login required."));

        // Regular users can only see their own submissions; admins can see all
        var query = _context.Submissions.AsNoTracking();

        if (user.Role < Role.Admin)
            query = query.Where(s => s.UserId == user.Id);

        if (challengeId.HasValue)
            query = query.Where(s => s.ChallengeId == challengeId.Value);

        if (userId.HasValue && user.Role >= Role.Admin)
            query = query.Where(s => s.UserId == userId.Value);

        if (submissionType.HasValue)
            query = query.Where(s => s.SubmissionType == submissionType.Value);

        var total = await query.CountAsync(token);

        var submissions = await query
            .OrderByDescending(s => s.SubmitTimeUtc)
            .Skip(skip)
            .Take(count)
            .ToListAsync(token);

        var response = submissions.Select(SubmissionResponse.FromSubmission).ToList();

        return Ok(response.ToResponse(total));
    }

    /// <summary>
    /// Upload a file attachment for Writeup submissions.
    /// Accepts .pdf and .md files up to 50MB.
    /// </summary>
    [HttpPost("upload")]
    [RequireUser]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB
    [ProducesResponseType(typeof(SubmissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadWriteup(
        [Required] IFormFile file,
        [FromForm] int challengeId,
        [FromForm] int gameId,
        [FromForm] int teamId,
        [FromForm] int participationId,
        CancellationToken token)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new RequestResponse("No file provided."));

        // Validate file type
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (extension != ".pdf" && extension != ".md")
            return BadRequest(new RequestResponse("Only .pdf and .md files are accepted."));

        if (file.Length > 50 * 1024 * 1024)
            return BadRequest(new RequestResponse("File size exceeds 50MB limit."));

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(new RequestResponse("Login required."));

        // Find the scoring rule
        var rule = await _context.ScoringRules
            .FirstOrDefaultAsync(r => r.ChallengeId == challengeId
                && r.SubmissionType == ScoringSubmissionType.Writeup, token);

        if (rule is null)
            return BadRequest(new RequestResponse(
                "No Writeup scoring rule found for this challenge."));

        // Check attempt limits
        if (rule.MaxAttempts > 0)
        {
            var attemptCount = await _context.Submissions
                .CountAsync(s => s.ChallengeId == challengeId
                    && s.UserId == user.Id
                    && s.SubmissionType == ScoringSubmissionType.Writeup, token);

            if (attemptCount >= rule.MaxAttempts)
                return BadRequest(new RequestResponse(
                    $"Maximum attempts ({rule.MaxAttempts}) reached for Writeup submissions."));
        }

        // Read file content
        string fileContent;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            fileContent = await reader.ReadToEndAsync(token);
        }

        // Build JSON content with file metadata
        var content = JsonSerializer.Serialize(new
        {
            fileName = file.FileName,
            fileSize = file.Length,
            contentType = file.ContentType,
            body = fileContent
        });

        var attemptNumber = await _context.Submissions
            .CountAsync(s => s.ChallengeId == challengeId
                && s.UserId == user.Id
                && s.SubmissionType == ScoringSubmissionType.Writeup, token) + 1;

        var submission = new Submission
        {
            Answer = file.FileName, // file name as answer placeholder
            Status = AnswerResult.FlagSubmitted, // Pending review
            SubmissionType = ScoringSubmissionType.Writeup,
            Content = content,
            AttemptNumber = attemptNumber,
            Score = 0,
            SubmitTimeUtc = DateTimeOffset.UtcNow,
            UserId = user.Id,
            ChallengeId = challengeId,
            GameId = gameId,
            TeamId = teamId,
            ParticipationId = participationId
        };

        await _context.Submissions.AddAsync(submission, token);
        await _context.SaveChangesAsync(token);

        _logger.LogInformation(
            "Writeup submission {SubmissionId} uploaded: File={FileName}, Size={Size}, User={UserId}",
            submission.Id, file.FileName, file.Length, user.Id);

        return Ok(SubmissionResponse.FromSubmission(submission));
    }

    #endregion

    #region Admin Review

    /// <summary>
    /// List submissions pending manual review. Admin only.
    /// </summary>
    [HttpGet("pending-review")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ArrayResponse<SubmissionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingReviews(
        [FromQuery] int? challengeId,
        [FromQuery] int count = 50,
        [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var query = _context.Submissions
            .AsNoTracking()
            .Where(s => s.Status == AnswerResult.FlagSubmitted
                && (s.SubmissionType == ScoringSubmissionType.Writeup
                    || s.SubmissionType == ScoringSubmissionType.Custom));

        if (challengeId.HasValue)
            query = query.Where(s => s.ChallengeId == challengeId.Value);

        var total = await query.CountAsync(token);

        var submissions = await query
            .OrderBy(s => s.SubmitTimeUtc)
            .Skip(skip)
            .Take(count)
            .ToListAsync(token);

        var response = submissions.Select(SubmissionResponse.FromSubmission).ToList();

        return Ok(response.ToResponse(total));
    }

    /// <summary>
    /// Submit a manual review for a pending submission. Admin only.
    /// </summary>
    [HttpPost("{id:int}/review")]
    [RequireAdmin]
    [ProducesResponseType(typeof(SubmissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitReview(
        [FromRoute] int id,
        [FromBody] ReviewRequest request,
        CancellationToken token)
    {
        var submission = await _context.Submissions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id, token);

        if (submission is null)
            return NotFound(new RequestResponse("Submission not found.",
                StatusCodes.Status404NotFound));

        if (submission.Status != AnswerResult.FlagSubmitted)
            return BadRequest(new RequestResponse("Submission is not pending review."));

        var reviewer = await _userManager.GetUserAsync(User);
        if (reviewer is null)
            return Unauthorized(new RequestResponse("Login required."));

        // Apply review
        submission.Status = request.Accepted ? AnswerResult.Accepted : AnswerResult.WrongAnswer;
        submission.Score = request.Accepted ? (request.Score ?? 0) : 0;
        submission.ReviewComment = request.Comment;
        submission.ReviewedById = reviewer.Id;

        await _context.SaveChangesAsync(token);

        _logger.LogInformation(
            "Submission {SubmissionId} reviewed: Accepted={Accepted}, Score={Score}, Reviewer={ReviewerId}",
            submission.Id, request.Accepted, submission.Score, reviewer.Id);

        // T061: Broadcast updates if accepted
        if (submission.Status == AnswerResult.Accepted)
        {
            await BroadcastScoreAndLeaderboardAsync(submission.ChallengeId, submission.UserId ?? Guid.Empty);
        }

        return Ok(SubmissionResponse.FromSubmission(submission));
    }

    #endregion

    #region Verification Helpers

    /// <summary>
    /// Verify a submission based on the scoring rule's verification mode.
    /// </summary>
    private async Task<(AnswerResult Status, int Score)> VerifySubmissionAsync(
        SubmissionCreateRequest request,
        ScoringRule rule,
        UserInfo user,
        CancellationToken token)
    {
        var result = await _scoringEngine.ProcessSubmissionAsync(request, user.Id, token);
        return (result.Status, result.Score);
    }

    /// <summary>
    /// Broadcast ScoreUpdated and LeaderboardUpdated events via SignalR.
    /// </summary>
    private async Task BroadcastScoreAndLeaderboardAsync(int challengeId, Guid userId)
    {
        try
        {
            // Calculate updated score
            var totalScore = await _scoringService.CalculateTotalScoreAsync(challengeId, userId);
            var leaderboard = await _leaderboardService.GetLeaderboardAsync(challengeId);

            var groupName = $"scenario_{challengeId}";

            await _hubContext.Clients.Group(groupName)
                .SendAsync(ScenarioHub.ScoreUpdatedEvent, new
                {
                    userId = userId.ToString(),
                    challengeId,
                    totalScore
                });

            await _hubContext.Clients.Group(groupName)
                .SendAsync(ScenarioHub.LeaderboardUpdatedEvent, leaderboard);

            _logger.LogDebug("Broadcasted ScoreUpdated and LeaderboardUpdated for Challenge {ChallengeId}, User {UserId}",
                challengeId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast score/leaderboard update for Challenge {ChallengeId}",
                challengeId);
        }
    }

    #endregion
}

#region Request/Response Models

/// <summary>
/// Request model for creating a multi-type submission.
/// </summary>
public class SubmissionCreateRequest
{
    /// <summary>
    /// Submitted answer (flag, IP address, credential, etc.)
    /// </summary>
    [Required]
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Type of submission
    /// </summary>
    [Required]
    public ScoringSubmissionType SubmissionType { get; set; } = ScoringSubmissionType.Flag;

    /// <summary>
    /// Optional JSON content for the submission
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Challenge ID being submitted to
    /// </summary>
    [Required]
    public int ChallengeId { get; set; }

    /// <summary>
    /// Game ID
    /// </summary>
    [Required]
    public int GameId { get; set; }

    /// <summary>
    /// Team ID
    /// </summary>
    [Required]
    public int TeamId { get; set; }

    /// <summary>
    /// Participation ID
    /// </summary>
    [Required]
    public int ParticipationId { get; set; }
}

/// <summary>
/// Response model for a submission.
/// </summary>
public class SubmissionResponse
{
    public int Id { get; set; }
    public string Answer { get; set; } = string.Empty;
    public AnswerResult Status { get; set; }
    public ScoringSubmissionType SubmissionType { get; set; }
    public string? Content { get; set; }
    public int AttemptNumber { get; set; }
    public int Score { get; set; }
    public DateTimeOffset SubmitTimeUtc { get; set; }
    public int ChallengeId { get; set; }
    public string? ChallengeName { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public int TeamId { get; set; }
    public string? TeamName { get; set; }
    public string? ReviewComment { get; set; }
    public Guid? ReviewedById { get; set; }

    public static SubmissionResponse FromSubmission(Submission s) => new()
    {
        Id = s.Id,
        Answer = s.Answer,
        Status = s.Status,
        SubmissionType = s.SubmissionType,
        Content = s.Content,
        AttemptNumber = s.AttemptNumber,
        Score = s.Score,
        SubmitTimeUtc = s.SubmitTimeUtc,
        ChallengeId = s.ChallengeId,
        ChallengeName = s.GameChallenge?.Title,
        UserId = s.UserId,
        UserName = s.User?.UserName,
        TeamId = s.TeamId,
        TeamName = s.Team?.Name,
        ReviewComment = s.ReviewComment,
        ReviewedById = s.ReviewedById
    };
}

/// <summary>
/// Request model for submitting a manual review.
/// </summary>
public class ReviewRequest
{
    /// <summary>
    /// Whether the submission is accepted
    /// </summary>
    [Required]
    public bool Accepted { get; set; }

    /// <summary>
    /// Score to award (only meaningful if Accepted)
    /// </summary>
    public int? Score { get; set; }

    /// <summary>
    /// Reviewer comment/feedback
    /// </summary>
    [MaxLength(1024)]
    public string? Comment { get; set; }
}

#endregion
