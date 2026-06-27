using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GZCTF.Controllers;

/// <summary>
/// Time slot management for scenario challenges.
/// Handles listing available time slots and reserving spots.
/// </summary>
[ApiController]
[Route("api/v1/scenarios")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class TimeSlotController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<UserInfo> _userManager;
    private readonly ILogger<TimeSlotController> _logger;
    private readonly IStringLocalizer<Program> _localizer;

    public TimeSlotController(
        AppDbContext dbContext,
        UserManager<UserInfo> userManager,
        ILogger<TimeSlotController> logger,
        IStringLocalizer<Program> localizer)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Get available time slots for a scenario
    /// </summary>
    /// <param name="id">Scenario ID</param>
    /// <param name="token">Cancellation token</param>
    /// <response code="200">List of available time slots</response>
    /// <response code="404">Scenario not found</response>
    [RequireUser]
    [HttpGet("{id:int}/timeslots")]
    [ProducesResponseType(typeof(TimeSlotResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeSlots([FromRoute] int id, CancellationToken token)
    {
        var scenario = await _dbContext.GameChallenges
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.Scenario, token);

        if (scenario is null)
            return NotFound(new RequestResponse(_localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(
                new RequestResponse(_localizer[nameof(Resources.Program.Auth_LoginRequired)]));

        if (!await CanAccessGameAsync(user, scenario.GameId, token))
            return Forbid();

        var now = DateTimeOffset.UtcNow;

        var slots = await _dbContext.TimeSlots
            .AsNoTracking()
            .Where(t => t.ScenarioId == id && t.EndTime > now)
            .OrderBy(t => t.StartTime)
            .Select(t => new TimeSlotResponse
            {
                Id = t.Id,
                StartTime = t.StartTime,
                EndTime = t.EndTime,
                MaxParticipants = t.MaxParticipants,
                CurrentParticipants = t.CurrentParticipants,
                IsFull = t.CurrentParticipants >= t.MaxParticipants,
                IsAvailable = t.StartTime <= now && t.CurrentParticipants < t.MaxParticipants
            })
            .ToListAsync(token);

        return Ok(slots);
    }

    /// <summary>
    /// Reserve a spot in a time slot for the current user
    /// </summary>
    /// <param name="id">Scenario ID</param>
    /// <param name="slotId">Time slot ID to reserve</param>
    /// <param name="token">Cancellation token</param>
    /// <response code="200">Successfully reserved</response>
    /// <response code="400">Slot is full, expired, or scenario not found</response>
    [RequireUser]
    [HttpPost("{id:int}/timeslots/{slotId:int}/reserve")]
    [ProducesResponseType(typeof(ReservationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReserveSlot([FromRoute] int id, [FromRoute] int slotId,
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

        var timeSlot = await _dbContext.TimeSlots
            .FirstOrDefaultAsync(t => t.Id == slotId && t.ScenarioId == id, token);

        if (timeSlot is null)
            return NotFound(new RequestResponse("Time slot not found.",
                StatusCodes.Status404NotFound));

        if (timeSlot.StartTime > DateTimeOffset.UtcNow)
            return BadRequest(new RequestResponse("This time slot has not started yet."));

        if (timeSlot.EndTime < DateTimeOffset.UtcNow)
            return BadRequest(new RequestResponse("This time slot has already ended."));

        if (timeSlot.CurrentParticipants >= timeSlot.MaxParticipants)
            return BadRequest(new RequestResponse("This time slot is full."));

        // Check if user already has an active instance for this scenario
        var existingInstance = await _dbContext.ScenarioInstances
            .AnyAsync(i => i.ScenarioId == id && i.UserId == user.Id &&
                i.Status == ScenarioInstanceStatus.Active, token);

        if (existingInstance)
            return BadRequest(new RequestResponse(
                "You already have an active instance for this scenario. Complete or abandon it before reserving a new slot."));

        // Reservation is just validation at this point - the actual instance creation
        // decrements the counter in CreateInstance. Here we return availability info.
        _logger.LogInformation("Slot reservation validated for User {UserId}, Scenario {ScenarioId}, Slot {SlotId}",
            user.Id, id, slotId);

        return Ok(new ReservationResult
        {
            SlotId = slotId,
            ScenarioId = id,
            StartTime = timeSlot.StartTime,
            EndTime = timeSlot.EndTime,
            AvailableSpots = timeSlot.MaxParticipants - timeSlot.CurrentParticipants,
            Message = "Time slot is available. Proceed to create a scenario instance."
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
/// Response model for a time slot
/// </summary>
public class TimeSlotResponse
{
    /// <summary>
    /// Time slot ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Slot start time
    /// </summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// Slot end time
    /// </summary>
    public DateTimeOffset EndTime { get; set; }

    /// <summary>
    /// Maximum number of participants
    /// </summary>
    public int MaxParticipants { get; set; }

    /// <summary>
    /// Current number of registered participants
    /// </summary>
    public int CurrentParticipants { get; set; }

    /// <summary>
    /// Whether the slot is full
    /// </summary>
    public bool IsFull { get; set; }

    /// <summary>
    /// Whether the slot is available to join now
    /// </summary>
    public bool IsAvailable { get; set; }
}

/// <summary>
/// Result of a time slot reservation
/// </summary>
public class ReservationResult
{
    public int SlotId { get; set; }
    public int ScenarioId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int AvailableSpots { get; set; }
    public string Message { get; set; } = string.Empty;
}
