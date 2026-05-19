using System.Diagnostics;
using System.Text.Json;
using GZCTF.Hubs;
using GZCTF.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

/// <summary>
/// Background service that periodically checks uncompleted auto-verifiable checkpoints
/// on active IR challenge instances. Pushes real-time updates via SignalR.
/// </summary>
public class CheckpointVerificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ScenarioHub> _hubContext;
    private readonly ILogger<CheckpointVerificationService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    public CheckpointVerificationService(
        IServiceScopeFactory scopeFactory,
        IHubContext<ScenarioHub> hubContext,
        ILogger<CheckpointVerificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CheckpointVerificationService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessActiveInstancesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing IR checkpoint verifications");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("CheckpointVerificationService stopped");
    }

    private async Task ProcessActiveInstancesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Get all active IR instances (status = Ready)
        var activeInstances = await context.IRInstances
            .Include(i => i.Challenge)
            .Where(i => i.EnvironmentStatus == EnvironmentStatus.Ready)
            .ToListAsync(cancellationToken);

        if (activeInstances.Count == 0)
            return;

        foreach (var instance in activeInstances)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await ProcessInstanceCheckpointsAsync(context, instance, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing instance {InstanceId}", instance.Id);
            }
        }
    }

    private async Task ProcessInstanceCheckpointsAsync(
        AppDbContext context,
        IRInstance instance,
        CancellationToken cancellationToken)
    {
        // Get checkpoints for this challenge that are auto-verifiable
        var autoCheckpoints = await context.IRCheckpoints
            .Where(c => c.ChallengeId == instance.ChallengeId
                && (c.VerificationType == VerificationType.AutoCommand
                    || c.VerificationType == VerificationType.AutoScript))
            .OrderBy(c => c.OrderIndex)
            .ToListAsync(cancellationToken);

        if (autoCheckpoints.Count == 0)
            return;

        // Parse current checkpoint results
        var checkpointResults = ParseCheckpointResults(instance.CheckpointResults);
        var anyCompleted = false;

        foreach (var checkpoint in autoCheckpoints)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var checkpointKey = checkpoint.Id.ToString();

            // Skip already completed checkpoints
            if (checkpointResults.TryGetValue(checkpointKey, out var result) && result.Completed)
                continue;

            // Try to verify the checkpoint
            var verified = await VerifyCheckpointAsync(checkpoint, instance, cancellationToken);

            if (verified)
            {
                checkpointResults[checkpointKey] = new CheckpointResultEntry
                {
                    Completed = true,
                    Score = checkpoint.Score,
                    VerifiedAt = DateTimeOffset.UtcNow
                };

                anyCompleted = true;

                // Write Submission for leaderboard
                var challenge = await context.GameChallenges.FindAsync([instance.ChallengeId], cancellationToken);
                if (challenge is not null)
                {
                    context.Submissions.Add(new Submission
                    {
                        Answer = $"auto-cp-{checkpoint.Id}",
                        Status = AnswerResult.Accepted,
                        SubmissionType = ScoringSubmissionType.Flag,
                        AttemptNumber = 1, Score = checkpoint.Score,
                        SubmitTimeUtc = DateTimeOffset.UtcNow,
                        UserId = instance.UserId, ChallengeId = instance.ChallengeId,
                        GameId = challenge.GameId, TeamId = 0, ParticipationId = 0
                    });
                }

                _logger.LogInformation(
                    "Checkpoint {CheckpointId} completed for instance {InstanceId} (Score: {Score})",
                    checkpoint.Id, instance.Id, checkpoint.Score);

                // Notify via SignalR
                await _hubContext.Clients
                    .Group($"ir_{instance.ChallengeId}")
                    .SendAsync(ScenarioHub.CheckpointCompletedEvent, new
                    {
                        InstanceId = instance.Id,
                        CheckpointId = checkpoint.Id,
                        Score = checkpoint.Score,
                        IsRequired = checkpoint.IsRequired
                    }, cancellationToken);
            }
        }

        if (anyCompleted)
        {
            instance.CheckpointResults = JsonSerializer.Serialize(checkpointResults);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> VerifyCheckpointAsync(
        IRCheckpoint checkpoint,
        IRInstance instance,
        CancellationToken cancellationToken)
    {
        var config = ParseVerificationConfig(checkpoint.VerificationConfig);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CheckTimeout);

        try
        {
            return checkpoint.VerificationType switch
            {
                VerificationType.AutoCommand => await VerifyAutoCommandAsync(config, instance, cts.Token),
                VerificationType.AutoScript => await VerifyAutoScriptAsync(config, instance, cts.Token),
                _ => false
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Checkpoint {CheckpointId} verification timed out for instance {InstanceId}",
                checkpoint.Id, instance.Id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Checkpoint {CheckpointId} verification failed for instance {InstanceId}",
                checkpoint.Id, instance.Id);
            return false;
        }
    }

    private async Task<bool> VerifyAutoCommandAsync(
        Dictionary<string, object?> config,
        IRInstance instance,
        CancellationToken cancellationToken)
    {
        var command = config.GetValueOrDefault("Command")?.ToString();
        var expectedOutput = config.GetValueOrDefault("ExpectedOutput")?.ToString();
        var matchType = config.GetValueOrDefault("MatchType")?.ToString() ?? "Contains";

        if (string.IsNullOrEmpty(command))
        {
            _logger.LogWarning("AutoCommand checkpoint missing Command config for instance {InstanceId}", instance.Id);
            return false;
        }

        // Read SSH connection info from instance access details
        var sshHost = GetAccessDetail(instance, "SshHost") ?? "localhost";
        var sshPort = GetAccessDetailInt(instance, "SshPort") ?? 22;
        var sshUsername = GetAccessDetail(instance, "SshUsername") ?? "player";

        var args = $"-o StrictHostKeyChecking=no -o ConnectTimeout=10 -p {sshPort} {sshUsername}@{sshHost} {EscapeArg(command)}";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ssh",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var completedTask = await Task.WhenAny(
            outputTask,
            Task.Delay(CheckTimeout, cancellationToken));

        if (completedTask != outputTask)
        {
            process.Kill(entireProcessTree: true);
            return false;
        }

        var output = await outputTask;
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogDebug("AutoCommand exited with code {ExitCode} for instance {InstanceId}: {Command}",
                process.ExitCode, instance.Id, command);
            return false;
        }

        if (string.IsNullOrEmpty(expectedOutput))
            return process.ExitCode == 0;

        return matchType switch
        {
            "Exact" => output.Trim() == expectedOutput.Trim(),
            "Regex" => System.Text.RegularExpressions.Regex.IsMatch(output.Trim(), expectedOutput),
            _ => output.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase)
        };
    }

    private async Task<bool> VerifyAutoScriptAsync(
        Dictionary<string, object?> config,
        IRInstance instance,
        CancellationToken cancellationToken)
    {
        var scriptPath = config.GetValueOrDefault("ScriptPath")?.ToString();
        var scriptArgs = config.GetValueOrDefault("ScriptArgs")?.ToString() ?? "";

        if (string.IsNullOrEmpty(scriptPath))
        {
            _logger.LogWarning("AutoScript checkpoint missing ScriptPath for instance {InstanceId}",
                instance.Id);
            return false;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = scriptPath,
                    Arguments = $"--instance-id {instance.Id} {scriptArgs}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            process.Start();
            await process.WaitForExitAsync(cts.Token);

            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AutoScript timed out for instance {InstanceId}", instance.Id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoScript failed for instance {InstanceId}", instance.Id);
            return false;
        }
    }

    private static string? GetAccessDetail(IRInstance instance, string key)
    {
        if (string.IsNullOrEmpty(instance.AccessDetails))
            return null;

        try
        {
            var details = JsonSerializer.Deserialize<Dictionary<string, object?>>(instance.AccessDetails);
            return details?.GetValueOrDefault(key)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int? GetAccessDetailInt(IRInstance instance, string key)
    {
        var value = GetAccessDetail(instance, key);
        if (value is not null && int.TryParse(value, out var intValue))
            return intValue;
        return null;
    }

    private static Dictionary<string, CheckpointResultEntry> ParseCheckpointResults(string json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, CheckpointResultEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, object?> ParseVerificationConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string EscapeArg(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;
}

/// <summary>
/// Represents a single checkpoint result stored in the CheckpointResults JSON.
/// </summary>
public class CheckpointResultEntry
{
    public bool Completed { get; set; }
    public int Score { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}
