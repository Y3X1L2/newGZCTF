using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Hubs;

/// <summary>
/// SignalR hub for real-time CTF scenario events.
/// Players join scenario-specific groups to receive stage unlock notifications,
/// time warnings, score updates, and environment status changes.
/// </summary>
public class ScenarioHub(AppDbContext dbContext) : Hub
{
    /// <summary>
    /// Event fired when a new stage is unlocked in a scenario.
    /// </summary>
    public const string StageUnlockedEvent = "StageUnlocked";

    /// <summary>
    /// Event fired when the remaining time reaches a warning threshold.
    /// </summary>
    public const string TimeWarningEvent = "TimeWarning";

    /// <summary>
    /// Event fired when a team's score is updated.
    /// </summary>
    public const string ScoreUpdatedEvent = "ScoreUpdated";

    /// <summary>
    /// Event fired when the VM environment is ready for the player.
    /// </summary>
    public const string EnvironmentReadyEvent = "EnvironmentReady";

    /// <summary>
    /// Event fired when a checkpoint objective is completed.
    /// </summary>
    public const string CheckpointCompletedEvent = "CheckpointCompleted";

    /// <summary>
    /// Event fired when the environment reset is complete.
    /// </summary>
    public const string EnvironmentResetCompleteEvent = "EnvironmentResetComplete";

    /// <summary>
    /// Event fired when the leaderboard rankings are updated.
    /// </summary>
    public const string LeaderboardUpdatedEvent = "LeaderboardUpdated";

    /// <summary>
    /// Event fired when a shell log entry is updated.
    /// </summary>
    public const string ShellLogUpdatedEvent = "ShellLogUpdated";

    /// <summary>
    /// Adds the calling connection to the SignalR group for the specified scenario,
    /// enabling it to receive real-time scenario events.
    /// </summary>
    /// <param name="scenarioId">The unique identifier of the scenario to join.</param>
    [Authorize]
    public async Task JoinScenarioGroup(string scenarioId)
    {
        if (!Guid.TryParse(scenarioId, out var instanceId) ||
            !Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new HubException("Invalid scenario subscription.");

        var scenario = await GetScenarioId(instanceId, userId);

        if (scenario > 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"scenario_{scenario}", Context.ConnectionAborted);
            return;
        }

        throw new HubException("Scenario subscription is not allowed.");
    }

    /// <summary>
    /// Removes the calling connection from the SignalR group for the specified scenario.
    /// </summary>
    /// <param name="scenarioId">The unique identifier of the scenario to leave.</param>
    [Authorize]
    public async Task LeaveScenarioGroup(string scenarioId)
    {
        if (!Guid.TryParse(scenarioId, out var instanceId) ||
            !Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return;

        var scenario = await GetScenarioId(instanceId, userId);

        if (scenario > 0)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"scenario_{scenario}", Context.ConnectionAborted);
    }

    [Authorize]
    public async Task JoinIRGroup(string instanceId)
    {
        if (!Guid.TryParse(instanceId, out var id) ||
            !Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new HubException("Invalid IR subscription.");

        var irChallenge = await GetIrChallengeId(id, userId);
        if (irChallenge > 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"ir_{irChallenge}", Context.ConnectionAborted);
            return;
        }

        throw new HubException("IR subscription is not allowed.");
    }

    [Authorize]
    public async Task LeaveIRGroup(string instanceId)
    {
        if (!Guid.TryParse(instanceId, out var id) ||
            !Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return;

        var irChallenge = await GetIrChallengeId(id, userId);
        if (irChallenge > 0)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ir_{irChallenge}", Context.ConnectionAborted);
    }

    Task<int> GetScenarioId(Guid instanceId, Guid userId) =>
        dbContext.ScenarioInstances
            .AsNoTracking()
            .Where(i => i.Id == instanceId && i.UserId == userId)
            .Select(i => i.ScenarioId)
            .SingleOrDefaultAsync(Context.ConnectionAborted);

    Task<int> GetIrChallengeId(Guid instanceId, Guid userId) =>
        dbContext.IRInstances
            .AsNoTracking()
            .Where(i => i.Id == instanceId && i.UserId == userId)
            .Select(i => i.ChallengeId)
            .SingleOrDefaultAsync(Context.ConnectionAborted);
}
