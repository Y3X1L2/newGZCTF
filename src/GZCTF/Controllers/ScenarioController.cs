using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using GZCTF.Extensions;
using GZCTF.Hubs;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Services;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NSwag.Annotations;

namespace GZCTF.Controllers;

/// <summary>
/// Scenario challenge management APIs.
/// Handles CRUD for multi-stage attack chain scenarios and player instance lifecycle.
/// </summary>
[ApiController]
[Route("api/v1/scenarios")]
[LegacyFeatureGone("独立 Scenario 模块已停用，请在 CTF 题型分类中使用 IR/Scenario 方向。")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class ScenarioController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<UserInfo> _userManager;
    private readonly EnvironmentService _environmentService;
    private readonly IHubContext<Hubs.ScenarioHub> _scenarioHub;
    private readonly ILogger<ScenarioController> _logger;
    private readonly IStringLocalizer<Program> _localizer;

    public ScenarioController(
        AppDbContext dbContext,
        UserManager<UserInfo> userManager,
        EnvironmentService environmentService,
        IHubContext<Hubs.ScenarioHub> scenarioHub,
        ILogger<ScenarioController> logger,
        IStringLocalizer<Program> localizer)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _environmentService = environmentService;
        _scenarioHub = scenarioHub;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Create a new scenario challenge
    /// </summary>
    /// <remarks>
    /// Creates a GameChallenge with Type=Scenario and associated stages. Requires Admin or Author role.
    /// </remarks>
    [RequireAdmin]
    [HttpPost]
    [ProducesResponseType(typeof(ScenarioDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateScenario([FromBody] ScenarioCreateModel model,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(
                new RequestResponse(_localizer[nameof(Resources.Program.Model_TitleRequired)]));

        var game = await _dbContext.Games.FindAsync([model.GameId], token);
        if (game is null)
            return NotFound(new RequestResponse(_localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var scenario = new GameChallenge
        {
            Title = model.Title,
            Content = model.Description ?? string.Empty,
            Category = ChallengeCategory.Scenario,
            Type = ChallengeType.Scenario,
            IsEnabled = false,
            GameId = model.GameId,
            Game = game,
            OriginalScore = 1000
        };

        await _dbContext.GameChallenges.AddAsync(scenario, token);
        await _dbContext.SaveChangesAsync(token);

        if (model.Stages is { Count: > 0 })
        {
            for (var i = 0; i < model.Stages.Count; i++)
            {
                var stageModel = model.Stages[i];
                var stage = new Stage
                {
                    ScenarioId = scenario.Id,
                    OrderIndex = i,
                    Title = stageModel.Title,
                    SkillDescription = stageModel.SkillDescription,
                    NetworkRules = JsonSerializer.Serialize(stageModel.NetworkRules ?? []),
                    PrerequisiteStageIds = JsonSerializer.Serialize(stageModel.PrerequisiteStageIds ?? []),
                    EnvironmentImageIds = JsonSerializer.Serialize(stageModel.EnvironmentImageIds ?? [])
                };
                stage.SetFlag(stageModel.Flag);

                await _dbContext.Stages.AddAsync(stage, token);
            }
        }

        if (model.ScoringRules is { Count: > 0 })
        {
            foreach (var ruleModel in model.ScoringRules)
            {
                var rule = new ScoringRule
                {
                    ChallengeId = scenario.Id,
                    SubmissionType = ruleModel.SubmissionType,
                    Weight = ruleModel.Weight,
                    VerificationMode = ruleModel.VerificationMode,
                    MaxAttempts = ruleModel.MaxAttempts,
                    ScoreDecay = ruleModel.ScoreDecay,
                    ExpectedAnswerHash = ruleModel.ExpectedAnswerHash,
                    VerificationConfig = ruleModel.VerificationConfig
                };
                await _dbContext.ScoringRules.AddAsync(rule, token);
            }
        }

        await _dbContext.SaveChangesAsync(token);

        _logger.LogInformation("Scenario '{Title}' (ID: {Id}) created in Game {GameId}",
            scenario.Title, scenario.Id, model.GameId);

        return Ok(ScenarioDetailModel.FromChallenge(scenario));
    }

    /// <summary>
    /// List scenarios, optionally filtered by game
    /// </summary>
    [RequireAdmin]
    [HttpGet]
    [ProducesResponseType(typeof(ArrayResponse<ScenarioListModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListScenarios(
        [FromQuery] int? gameId,
        [FromQuery][Range(0, 100)] int count = 20,
        [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var query = _dbContext.GameChallenges
            .AsNoTracking()
            .Where(c => c.Type == ChallengeType.Scenario);

        if (gameId.HasValue)
            query = query.Where(c => c.GameId == gameId.Value);

        var total = await query.CountAsync(token);

        var scenarios = await query
            .OrderByDescending(c => c.Id)
            .Skip(skip)
            .Take(count)
            .Select(c => new ScenarioListModel
            {
                Id = c.Id,
                Title = c.Title,
                GameId = c.GameId,
                IsEnabled = c.IsEnabled,
                StageCount = _dbContext.Stages.Count(s => s.ScenarioId == c.Id)
            })
            .ToListAsync(token);

        return Ok(scenarios.ToResponse(total));
    }

    /// <summary>
    /// Get scenario details including stages
    /// </summary>
    [RequireAdmin]
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ScenarioDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScenario([FromRoute] int id, CancellationToken token)
    {
        var scenario = await _dbContext.GameChallenges
            .AsNoTracking()
            .Include(c => c.Game)
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.Scenario, token);

        if (scenario is null)
            return NotFound(new RequestResponse(_localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var stages = await _dbContext.Stages
            .AsNoTracking()
            .Where(s => s.ScenarioId == id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync(token);

        var scoringRules = await _dbContext.ScoringRules
            .AsNoTracking()
            .Where(r => r.ChallengeId == id)
            .ToListAsync(token);

        return Ok(ScenarioDetailModel.FromChallenge(scenario, stages, scoringRules));
    }

    /// <summary>
    /// Update scenario information (only when in draft state)
    /// </summary>
    [RequireAdmin]
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ScenarioDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateScenario([FromRoute] int id,
        [FromBody] ScenarioUpdateModel model, CancellationToken token)
    {
        var scenario = await _dbContext.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.Scenario, token);

        if (scenario is null)
            return NotFound(new RequestResponse(_localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (scenario.IsEnabled)
            return BadRequest(
                new RequestResponse("Cannot update a published scenario. Unpublish it first."));

        if (model.Title is not null)
            scenario.Title = model.Title;
        if (model.Description is not null)
            scenario.Content = model.Description;

        await _dbContext.SaveChangesAsync(token);

        var stages = await _dbContext.Stages
            .Where(s => s.ScenarioId == id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync(token);

        var scoringRules = await _dbContext.ScoringRules
            .Where(r => r.ChallengeId == id)
            .ToListAsync(token);

        _logger.LogInformation("Scenario {Id} updated", id);

        return Ok(ScenarioDetailModel.FromChallenge(scenario, stages, scoringRules));
    }

    /// <summary>
    /// Delete scenario (only when in draft state)
    /// </summary>
    [RequireAdmin]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteScenario([FromRoute] int id, CancellationToken token)
    {
        var scenario = await _dbContext.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.Scenario, token);

        if (scenario is null)
            return NotFound(new RequestResponse(_localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (scenario.IsEnabled)
            return BadRequest(
                new RequestResponse("Cannot delete a published scenario. Unpublish it first."));

        // Remove associated stages and scoring rules
        var stages = await _dbContext.Stages.Where(s => s.ScenarioId == id).ToListAsync(token);
        _dbContext.Stages.RemoveRange(stages);

        var rules = await _dbContext.ScoringRules.Where(r => r.ChallengeId == id).ToListAsync(token);
        _dbContext.ScoringRules.RemoveRange(rules);

        // Remove associated time slots
        var slots = await _dbContext.TimeSlots.Where(t => t.ScenarioId == id).ToListAsync(token);
        _dbContext.TimeSlots.RemoveRange(slots);

        _dbContext.GameChallenges.Remove(scenario);
        await _dbContext.SaveChangesAsync(token);

        _logger.LogInformation("Scenario {Id} deleted", id);

        return Ok();
    }

    /// <summary>
    /// Publish scenario (set IsEnabled=true)
    /// </summary>
    [RequireAdmin]
    [HttpPost("{id:int}/publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PublishScenario([FromRoute] int id, CancellationToken token)
    {
        var scenario = await _dbContext.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.Scenario, token);

        if (scenario is null)
            return NotFound(new RequestResponse(_localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var stageCount = await _dbContext.Stages.CountAsync(s => s.ScenarioId == id, token);
        if (stageCount == 0)
            return BadRequest(new RequestResponse("Scenario must have at least one stage to publish."));

        scenario.IsEnabled = true;
        await _dbContext.SaveChangesAsync(token);

        _logger.LogInformation("Scenario {Id} published", id);

        return Ok(new { scenario.Id, scenario.IsEnabled });
    }

    /// <summary>
    /// Create a player instance for a scenario
    /// </summary>
    [RequireUser]
    [HttpPost("{id:int}/instances")]
    [ProducesResponseType(typeof(ScenarioInstanceModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateInstance([FromRoute] int id, [FromBody] CreateInstanceRequest request,
        CancellationToken token)
    {
        var scenario = await _dbContext.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.Scenario, token);

        if (scenario is null || !scenario.IsEnabled)
            return NotFound(new RequestResponse(_localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(
                new RequestResponse(_localizer[nameof(Resources.Program.Auth_LoginRequired)]));

        if (!await CanAccessGameAsync(user, scenario.GameId, token))
            return Forbid();

        // Validate time slot
        var timeSlot = await _dbContext.TimeSlots
            .FirstOrDefaultAsync(t => t.Id == request.TimeSlotId && t.ScenarioId == id, token);

        if (timeSlot is null)
            return BadRequest(new RequestResponse("Invalid time slot."));

        if (timeSlot.CurrentParticipants >= timeSlot.MaxParticipants)
            return BadRequest(new RequestResponse("Time slot is full."));

        if (timeSlot.StartTime > DateTimeOffset.UtcNow)
            return BadRequest(new RequestResponse("Time slot has not started yet."));

        if (timeSlot.EndTime < DateTimeOffset.UtcNow)
            return BadRequest(new RequestResponse("Time slot has ended."));

        // Check for existing active instance
        var existingInstance = await _dbContext.ScenarioInstances
            .FirstOrDefaultAsync(i => i.ScenarioId == id && i.UserId == user.Id &&
                i.Status == ScenarioInstanceStatus.Active, token);

        if (existingInstance is not null)
            return BadRequest(new RequestResponse(
                "You already have an active instance for this scenario."));

        // Get stages for this scenario
        var stages = await _dbContext.Stages
            .Where(s => s.ScenarioId == id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync(token);

        if (stages.Count == 0)
            return BadRequest(new RequestResponse("Scenario has no stages configured."));

        // Build initial stage statuses (first stage unlocked, rest locked)
        var stageStatuses = new Dictionary<int, StageStatus>();
        var firstStage = stages[0];
        stageStatuses[firstStage.Id] = StageStatus.Unlocked;

        foreach (var stage in stages.Skip(1))
            stageStatuses[stage.Id] = StageStatus.Locked;

        var timeline = new List<ScenarioTimelineEntry>
        {
            new()
            {
                Timestamp = DateTimeOffset.UtcNow,
                StageId = 0,
                EventType = "InstanceCreated",
                Details = "Scenario instance created"
            }
        };

        var instance = new ScenarioInstance
        {
            Id = Guid.NewGuid(),
            ScenarioId = id,
            UserId = user.Id,
            CurrentStageId = firstStage.Id,
            Status = ScenarioInstanceStatus.Active,
            StageStatuses = JsonSerializer.Serialize(stageStatuses),
            StageTimeline = JsonSerializer.Serialize(timeline),
            TimeSlotId = request.TimeSlotId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _dbContext.ScenarioInstances.AddAsync(instance, token);

        // Increment participant count on time slot
        timeSlot.CurrentParticipants++;
        _dbContext.TimeSlots.Update(timeSlot);

        await _dbContext.SaveChangesAsync(token);

        // Provision environment for the first stage
        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _environmentService.CreateStageEnvironmentAsync(
                        firstStage, user.Id, CancellationToken.None);

                    if (result is not null)
                    {
                        await _scenarioHub.Clients
                            .Group($"scenario_{id}")
                            .SendAsync(ScenarioHub.StageUnlockedEvent, new
                            {
                                instanceId = instance.Id,
                                stageId = firstStage.Id,
                                environment = result
                            });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to provision environment for Instance {InstanceId}, Stage {StageId}",
                        instance.Id, firstStage.Id);
                }
            }, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start environment provisioning for Instance {InstanceId}",
                instance.Id);
        }

        _logger.LogInformation("Scenario instance {InstanceId} created for User {UserId}, Scenario {ScenarioId}",
            instance.Id, user.Id, id);

        return Ok(ScenarioInstanceModel.FromInstance(instance, stages));
    }

    /// <summary>
    /// Get scenario instance status
    /// </summary>
    [RequireUser]
    [HttpGet("instances/{instanceId:guid}")]
    [ProducesResponseType(typeof(ScenarioInstanceModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInstanceStatus([FromRoute] Guid instanceId, CancellationToken token)
    {
        var instance = await _dbContext.ScenarioInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, token);

        if (instance is null)
            return NotFound(new RequestResponse("Instance not found.",
                StatusCodes.Status404NotFound));

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(
                new RequestResponse(_localizer[nameof(Resources.Program.Auth_LoginRequired)]));

        if (user.Role < Role.Admin && instance.UserId != user.Id)
            return Forbid();

        var stages = await _dbContext.Stages
            .AsNoTracking()
            .Where(s => s.ScenarioId == instance.ScenarioId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync(token);

        return Ok(ScenarioInstanceModel.FromInstance(instance, stages));
    }

    /// <summary>
    /// Submit a flag for a stage in a scenario instance
    /// </summary>
    [RequireUser]
    [HttpPost("instances/{instanceId:guid}/stages/{stageId:int}/submit")]
    [ProducesResponseType(typeof(StageSubmitResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitStageFlag(
        [FromRoute] Guid instanceId,
        [FromRoute] int stageId,
        [FromBody] FlagSubmitModel model,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(model.Flag))
            return BadRequest(new RequestResponse("Flag is required."));

        if (model.Flag.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse(_localizer[nameof(Resources.Program.Model_FlagTooLong)]));

        var instance = await _dbContext.ScenarioInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, token);

        if (instance is null)
            return NotFound(new RequestResponse("Instance not found.",
                StatusCodes.Status404NotFound));

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(
                new RequestResponse(_localizer[nameof(Resources.Program.Auth_LoginRequired)]));

        if (user.Role < Role.Admin && instance.UserId != user.Id)
            return Forbid();

        if (instance.Status != ScenarioInstanceStatus.Active)
            return BadRequest(new RequestResponse("This scenario instance is not active."));

        var stage = await _dbContext.Stages
            .FirstOrDefaultAsync(s => s.Id == stageId && s.ScenarioId == instance.ScenarioId, token);

        if (stage is null)
            return NotFound(new RequestResponse("Stage not found.",
                StatusCodes.Status404NotFound));

        // Check stage status
        var stageStatuses = DeserializeStageStatuses(instance.StageStatuses);
        if (!stageStatuses.TryGetValue(stageId, out var currentStatus))
            return BadRequest(new RequestResponse("Stage not found in this instance."));

        if (currentStatus == StageStatus.Locked)
            return BadRequest(new RequestResponse("This stage is locked."));

        if (currentStatus == StageStatus.Completed)
            return BadRequest(new RequestResponse("This stage has already been completed."));

        // Verify flag
        var isCorrect = stage.VerifyFlag(model.Flag);

        // Update stage status
        stageStatuses[stageId] = isCorrect ? StageStatus.Completed : StageStatus.Failed;
        instance.StageStatuses = JsonSerializer.Serialize(stageStatuses);

        // Add timeline entry
        var timeline = DeserializeTimeline(instance.StageTimeline);
        timeline.Add(new ScenarioTimelineEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            StageId = stageId,
            EventType = isCorrect ? "StageCompleted" : "StageFailed",
            Details = isCorrect ? "Flag submitted correctly" : "Incorrect flag submitted"
        });
        instance.StageTimeline = JsonSerializer.Serialize(timeline);

        if (isCorrect)
        {
            // Unlock next stage if applicable
            var stages = await _dbContext.Stages
                .Where(s => s.ScenarioId == instance.ScenarioId)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync(token);

            var currentStageIndex = stages.FindIndex(s => s.Id == stageId);
            if (currentStageIndex >= 0 && currentStageIndex + 1 < stages.Count)
            {
                var nextStage = stages[currentStageIndex + 1];
                stageStatuses[nextStage.Id] = StageStatus.Unlocked;
                instance.CurrentStageId = nextStage.Id;
                instance.StageStatuses = JsonSerializer.Serialize(stageStatuses);

                timeline.Add(new ScenarioTimelineEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    StageId = nextStage.Id,
                    EventType = "StageUnlocked",
                    Details = $"Stage '{nextStage.Title}' unlocked"
                });
                instance.StageTimeline = JsonSerializer.Serialize(timeline);

                // Notify via SignalR
                await _scenarioHub.Clients
                    .Group($"scenario_{instance.ScenarioId}")
                    .SendAsync(ScenarioHub.StageUnlockedEvent, new
                    {
                        instanceId = instance.Id,
                        stageId = nextStage.Id,
                        stageTitle = nextStage.Title
                    });
            }
            else
            {
                // All stages completed
                instance.Status = ScenarioInstanceStatus.Completed;

                await _scenarioHub.Clients
                    .Group($"scenario_{instance.ScenarioId}")
                    .SendAsync(ScenarioHub.CheckpointCompletedEvent, new
                    {
                        instanceId = instance.Id,
                        scenarioId = instance.ScenarioId
                    });
            }
        }

        await _dbContext.SaveChangesAsync(token);

        return Ok(new StageSubmitResult
        {
            IsCorrect = isCorrect,
            StageId = stageId,
            InstanceStatus = instance.Status,
            CurrentStageId = instance.CurrentStageId
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

    private static Dictionary<int, StageStatus> DeserializeStageStatuses(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, StageStatus>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static List<ScenarioTimelineEntry> DeserializeTimeline(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ScenarioTimelineEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

#region Request/Response Models

/// <summary>
/// Request model for creating a scenario
/// </summary>
public class ScenarioCreateModel
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public int GameId { get; set; }

    public List<StageCreateModel>? Stages { get; set; }

    public List<ScoringRuleCreateModel>? ScoringRules { get; set; }
}

/// <summary>
/// Request model for creating a stage within a scenario
/// </summary>
public class StageCreateModel
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string? SkillDescription { get; set; }

    [Required]
    public string Flag { get; set; } = string.Empty;

    public List<string>? NetworkRules { get; set; }

    public List<int>? PrerequisiteStageIds { get; set; }

    public List<int>? EnvironmentImageIds { get; set; }
}

/// <summary>
/// Request model for creating a scoring rule
/// </summary>
public class ScoringRuleCreateModel
{
    public ScoringSubmissionType SubmissionType { get; set; } = ScoringSubmissionType.Flag;

    [Range(0, 100)]
    public decimal Weight { get; set; } = 100;

    public VerificationMode VerificationMode { get; set; } = VerificationMode.AutoExact;

    public int MaxAttempts { get; set; }

    public ScoreDecay ScoreDecay { get; set; } = ScoreDecay.None;

    /// <summary>
    /// SHA256 hash of the expected answer (for AutoExact verification)
    /// </summary>
    public string? ExpectedAnswerHash { get; set; }

    /// <summary>
    /// JSON configuration for AutoRegex/AutoScript verification
    /// </summary>
    public string? VerificationConfig { get; set; }
}

/// <summary>
/// Request model for updating a scenario
/// </summary>
public class ScenarioUpdateModel
{
    public string? Title { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Request body for creating a scenario instance
/// </summary>
public class CreateInstanceRequest
{
    [Required]
    public int TimeSlotId { get; set; }
}

/// <summary>
/// Response model for a scenario list item
/// </summary>
public class ScenarioListModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int GameId { get; set; }
    public bool IsEnabled { get; set; }
    public int StageCount { get; set; }
}

/// <summary>
/// Response model for scenario details
/// </summary>
public class ScenarioDetailModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GameId { get; set; }
    public string? GameTitle { get; set; }
    public bool IsEnabled { get; set; }
    public ChallengeCategory Category { get; set; }
    public ChallengeType Type { get; set; }
    public List<StageDetailModel> Stages { get; set; } = [];
    public List<ScoringRuleDetailModel> ScoringRules { get; set; } = [];

    public static ScenarioDetailModel FromChallenge(GameChallenge challenge,
        List<Stage>? stages = null, List<ScoringRule>? scoringRules = null)
    {
        return new ScenarioDetailModel
        {
            Id = challenge.Id,
            Title = challenge.Title,
            Description = challenge.Content,
            GameId = challenge.GameId,
            GameTitle = challenge.Game?.Title,
            IsEnabled = challenge.IsEnabled,
            Category = challenge.Category,
            Type = challenge.Type,
            Stages = stages?.Select(StageDetailModel.FromStage).ToList() ?? [],
            ScoringRules = scoringRules?.Select(ScoringRuleDetailModel.FromRule).ToList() ?? []
        };
    }
}

/// <summary>
/// Stage detail in response
/// </summary>
public class StageDetailModel
{
    public int Id { get; set; }
    public int OrderIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SkillDescription { get; set; }
    public List<int>? PrerequisiteStageIds { get; set; }
    public List<int>? EnvironmentImageIds { get; set; }
    public int ScenarioId { get; set; }

    public static StageDetailModel FromStage(Stage stage)
    {
        return new StageDetailModel
        {
            Id = stage.Id,
            OrderIndex = stage.OrderIndex,
            Title = stage.Title,
            SkillDescription = stage.SkillDescription,
            PrerequisiteStageIds = DeserializeIntList(stage.PrerequisiteStageIds),
            EnvironmentImageIds = DeserializeIntList(stage.EnvironmentImageIds),
            ScenarioId = stage.ScenarioId
        };
    }

    private static List<int>? DeserializeIntList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Scoring rule detail in response
/// </summary>
public class ScoringRuleDetailModel
{
    public int Id { get; set; }
    public int ChallengeId { get; set; }
    public ScoringSubmissionType SubmissionType { get; set; }
    public decimal Weight { get; set; }
    public VerificationMode VerificationMode { get; set; }
    public int MaxAttempts { get; set; }
    public ScoreDecay ScoreDecay { get; set; }
    public string? ExpectedAnswerHash { get; set; }
    public string? VerificationConfig { get; set; }

    public static ScoringRuleDetailModel FromRule(ScoringRule rule)
    {
        return new ScoringRuleDetailModel
        {
            Id = rule.Id,
            ChallengeId = rule.ChallengeId,
            SubmissionType = rule.SubmissionType,
            Weight = rule.Weight,
            VerificationMode = rule.VerificationMode,
            MaxAttempts = rule.MaxAttempts,
            ScoreDecay = rule.ScoreDecay,
            ExpectedAnswerHash = rule.ExpectedAnswerHash,
            VerificationConfig = rule.VerificationConfig
        };
    }
}

/// <summary>
/// Response model for a scenario instance
/// </summary>
public class ScenarioInstanceModel
{
    public Guid Id { get; set; }
    public int ScenarioId { get; set; }
    public Guid UserId { get; set; }
    public int CurrentStageId { get; set; }
    public ScenarioInstanceStatus Status { get; set; }
    public Dictionary<int, StageStatus> StageStatuses { get; set; } = [];
    public List<ScenarioTimelineEntry> Timeline { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public int TimeSlotId { get; set; }
    public List<StageDetailModel> Stages { get; set; } = [];

    public static ScenarioInstanceModel FromInstance(ScenarioInstance instance, List<Stage> stages)
    {
        return new ScenarioInstanceModel
        {
            Id = instance.Id,
            ScenarioId = instance.ScenarioId,
            UserId = instance.UserId,
            CurrentStageId = instance.CurrentStageId,
            Status = instance.Status,
            StageStatuses = DeserializeStatuses(instance.StageStatuses),
            Timeline = DeserializeTimeline(instance.StageTimeline),
            CreatedAt = instance.CreatedAt,
            TimeSlotId = instance.TimeSlotId,
            Stages = stages.Select(StageDetailModel.FromStage).ToList()
        };
    }

    private static Dictionary<int, StageStatus> DeserializeStatuses(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, StageStatus>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static List<ScenarioTimelineEntry> DeserializeTimeline(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<ScenarioTimelineEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

/// <summary>
/// Response for flag submission to a stage
/// </summary>
public class StageSubmitResult
{
    public bool IsCorrect { get; set; }
    public int StageId { get; set; }
    public ScenarioInstanceStatus InstanceStatus { get; set; }
    public int CurrentStageId { get; set; }
}

#endregion
