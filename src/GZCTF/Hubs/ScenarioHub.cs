using Microsoft.AspNetCore.SignalR;

namespace GZCTF.Hubs;

/// <summary>
/// SignalR hub for real-time CTF scenario events.
/// Players join scenario-specific groups to receive stage unlock notifications,
/// time warnings, score updates, and environment status changes.
/// </summary>
public class ScenarioHub : Hub
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
    public async Task JoinScenarioGroup(string scenarioId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"scenario_{scenarioId}");

    /// <summary>
    /// Removes the calling connection from the SignalR group for the specified scenario.
    /// </summary>
    /// <param name="scenarioId">The unique identifier of the scenario to leave.</param>
    public async Task LeaveScenarioGroup(string scenarioId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"scenario_{scenarioId}");
}
