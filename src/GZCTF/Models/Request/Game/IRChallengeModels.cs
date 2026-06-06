using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Request.Game;

/// <summary>
/// Model for creating or updating an individual checkpoint.
/// </summary>
public class CheckpointCreateModel
{
    /// <summary>
    /// Display order within the challenge
    /// </summary>
    [Required]
    [Range(0, int.MaxValue)]
    public int OrderIndex { get; set; }

    /// <summary>
    /// Checkpoint description shown to the player
    /// </summary>
    [Required]
    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Score awarded for completing this checkpoint
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int Score { get; set; } = 100;

    /// <summary>
    /// Whether this checkpoint must be completed
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// How this checkpoint is verified
    /// </summary>
    [Required]
    public VerificationType VerificationType { get; set; } = VerificationType.ManualAnswer;

    /// <summary>
    /// JSON configuration for verification
    /// </summary>
    public string? VerificationConfig { get; set; }
}

/// <summary>
/// Model for creating an IR challenge with checkpoints.
/// </summary>
public class IRChallengeCreateModel
{
    /// <summary>
    /// Game ID
    /// </summary>
    [Required]
    public int GameId { get; set; }

    /// <summary>
    /// Challenge title
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Challenge content (Markdown supported)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Challenge category
    /// </summary>
    public ChallengeCategory Category { get; set; } = ChallengeCategory.IR;

    /// <summary>
    /// Whether the challenge is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Original score (base score for the challenge, checkpoint scores are additive)
    /// </summary>
    [Range(1, int.MaxValue)]
    public int OriginalScore { get; set; } = 1000;

    /// <summary>
    /// Minimum score rate
    /// </summary>
    [Range(0, 1)]
    public double MinScoreRate { get; set; } = 0.25;

    /// <summary>
    /// Difficulty coefficient
    /// </summary>
    public double Difficulty { get; set; } = 5;

    /// <summary>
    /// Container image for the IR environment
    /// </summary>
    public string? ContainerImage { get; set; }

    /// <summary>
    /// Memory limit in MB
    /// </summary>
    public int? MemoryLimit { get; set; } = 2048;

    /// <summary>
    /// CPU count (0.1 CPU units)
    /// </summary>
    public int? CPUCount { get; set; } = 2;

    /// <summary>
    /// Storage limit in MB
    /// </summary>
    public int? StorageLimit { get; set; } = 10240;

    /// <summary>
    /// VM template path for KVM-based IR environments
    /// </summary>
    public string? VmTemplatePath { get; set; }

    /// <summary>
    /// OS type for the target environment (Windows or Linux)
    /// </summary>
    [Required]
    public string OsType { get; set; } = "Linux";

    /// <summary>
    /// Checkpoints for this IR challenge
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<CheckpointCreateModel> Checkpoints { get; set; } = [];
}

/// <summary>
/// Model for updating an IR challenge.
/// </summary>
public class IRChallengeUpdateModel
{
    /// <summary>
    /// Challenge title
    /// </summary>
    [MinLength(1)]
    public string? Title { get; set; }

    /// <summary>
    /// Challenge content
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Challenge category
    /// </summary>
    public ChallengeCategory? Category { get; set; }

    /// <summary>
    /// Whether the challenge is enabled
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Original score
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? OriginalScore { get; set; }

    /// <summary>
    /// Minimum score rate
    /// </summary>
    [Range(0, 1)]
    public double? MinScoreRate { get; set; }

    /// <summary>
    /// Difficulty coefficient
    /// </summary>
    public double? Difficulty { get; set; }

    /// <summary>
    /// Container image
    /// </summary>
    public string? ContainerImage { get; set; }

    /// <summary>
    /// OS type for the target environment (Windows or Linux)
    /// </summary>
    public string? OsType { get; set; }

    /// <summary>
    /// Checkpoints to update (replaces existing)
    /// </summary>
    public List<CheckpointCreateModel>? Checkpoints { get; set; }
}

/// <summary>
/// Model for submitting a checkpoint answer (ManualAnswer type).
/// </summary>
public class CheckpointSubmitModel
{
    /// <summary>
    /// Player's submitted answer
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// Detailed checkpoint information in responses.
/// </summary>
public class CheckpointDetailModel
{
    public int Id { get; set; }
    public int OrderIndex { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool IsRequired { get; set; }
    public VerificationType VerificationType { get; set; }

    /// <summary>
    /// Whether the checkpoint is completed (for instance views)
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// When the checkpoint was verified (for instance views)
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; set; }

    internal static CheckpointDetailModel FromCheckpoint(IRCheckpoint checkpoint,
        CheckpointResultEntry? result = null) =>
        new()
        {
            Id = checkpoint.Id,
            OrderIndex = checkpoint.OrderIndex,
            Description = checkpoint.Description,
            Score = checkpoint.Score,
            IsRequired = checkpoint.IsRequired,
            VerificationType = checkpoint.VerificationType,
            IsCompleted = result?.Completed ?? false,
            VerifiedAt = result?.VerifiedAt
        };
}

/// <summary>
/// List item for IR challenges.
/// </summary>
public class IRChallengeListItemModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; }
    public bool IsEnabled { get; set; }
    public int OriginalScore { get; set; }
    public int CheckpointCount { get; set; }
    public string OsType { get; set; } = "Linux";
    public int GameId { get; set; }

    internal static IRChallengeListItemModel FromChallenge(GameChallenge challenge, int checkpointCount) =>
        new()
        {
            Id = challenge.Id,
            Title = challenge.Title,
            Content = challenge.Content,
            Category = challenge.Category,
            IsEnabled = challenge.IsEnabled,
            OriginalScore = challenge.OriginalScore,
            CheckpointCount = checkpointCount,
            OsType = challenge.OsType ?? (challenge.ContainerImage is not null ? "Linux" : "Windows"),
            GameId = challenge.GameId
        };
}

/// <summary>
/// Detailed IR challenge information with checkpoints.
/// </summary>
public class IRChallengeDetailModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; }
    public ChallengeType Type { get; set; }
    public bool IsEnabled { get; set; }
    public int OriginalScore { get; set; }
    public double MinScoreRate { get; set; }
    public double Difficulty { get; set; }
    public string? ContainerImage { get; set; }
    public int? MemoryLimit { get; set; }
    public int? CPUCount { get; set; }
    public int? StorageLimit { get; set; }
    public string? VmTemplatePath { get; set; }
    public string OsType { get; set; } = "Linux";
    public int GameId { get; set; }
    public int TotalCheckpointScore { get; set; }
    public List<CheckpointDetailModel> Checkpoints { get; set; } = [];

    internal static IRChallengeDetailModel FromChallenge(GameChallenge challenge,
        List<IRCheckpoint> checkpoints) =>
        new()
        {
            Id = challenge.Id,
            Title = challenge.Title,
            Content = challenge.Content,
            Category = challenge.Category,
            Type = challenge.Type,
            IsEnabled = challenge.IsEnabled,
            OriginalScore = challenge.OriginalScore,
            MinScoreRate = challenge.MinScoreRate,
            Difficulty = challenge.Difficulty,
            ContainerImage = challenge.ContainerImage,
            MemoryLimit = challenge.MemoryLimit,
            CPUCount = challenge.CPUCount,
            StorageLimit = challenge.StorageLimit,
            OsType = challenge.OsType ?? (challenge.ContainerImage is not null ? "Linux" : "Windows"),
            GameId = challenge.GameId,
            TotalCheckpointScore = checkpoints.Sum(c => c.Score),
            Checkpoints = checkpoints.Select(c => CheckpointDetailModel.FromCheckpoint(c)).ToList()
        };
}

/// <summary>
/// Detailed IR instance information with checkpoint progress.
/// </summary>
public class IRInstanceDetailModel
{
    public Guid Id { get; set; }
    public int ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public EnvironmentStatus EnvironmentStatus { get; set; }
    public int ResetCount { get; set; }
    public int TimeSlotId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? AccessDetails { get; set; }
    public int TotalScore { get; set; }
    public int CompletedCheckpoints { get; set; }
    public int TotalCheckpoints { get; set; }
    public List<CheckpointDetailModel> Checkpoints { get; set; } = [];

    internal static async Task<IRInstanceDetailModel> FromInstanceAsync(
        IRInstance instance,
        AppDbContext context,
        CancellationToken token = default)
    {
        var checkpoints = await context.Set<IRCheckpoint>()
            .Where(c => c.ChallengeId == instance.ChallengeId)
            .OrderBy(c => c.OrderIndex)
            .ToListAsync<IRCheckpoint>(token);

        var results = new Dictionary<string, CheckpointResultEntry>();
        if (!string.IsNullOrEmpty(instance.CheckpointResults))
        {
            try
            {
                results = JsonSerializer.Deserialize<Dictionary<string, CheckpointResultEntry>>(
                    instance.CheckpointResults) ?? [];
            }
            catch { }
        }

        var checkpointModels = checkpoints.Select(c =>
        {
            results.TryGetValue(c.Id.ToString(), out var result);
            return CheckpointDetailModel.FromCheckpoint(c, result);
        }).ToList();

        return new IRInstanceDetailModel
        {
            Id = instance.Id,
            ChallengeId = instance.ChallengeId,
            UserId = instance.UserId,
            EnvironmentStatus = instance.EnvironmentStatus,
            ResetCount = instance.ResetCount,
            TimeSlotId = instance.TimeSlotId,
            CreatedAt = instance.CreatedAt,
            EndedAt = instance.EndedAt,
            AccessDetails = instance.AccessDetails,
            CompletedCheckpoints = checkpointModels.Count(c => c.IsCompleted),
            TotalCheckpoints = checkpointModels.Count,
            TotalScore = checkpointModels.Where(c => c.IsCompleted).Sum(c => c.Score),
            Checkpoints = checkpointModels
        };
    }
}
