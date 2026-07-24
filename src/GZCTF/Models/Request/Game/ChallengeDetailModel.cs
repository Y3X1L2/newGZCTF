using GZCTF.Models.Request.Shared;

namespace GZCTF.Models.Request.Game;

/// <summary>
/// Multi-flag step metadata — exposed to players for guided solving.
/// Does NOT contain the actual flag values.
/// </summary>
public class FlagStepInfo
{
    public int Id { get; set; }
    public int OrderIndex { get; set; }
    public string? Description { get; set; }
}

public class ChallengeDetailModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;
    public List<string>? Hints { get; set; }
    public int Score { get; set; }
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;
    public EnvironmentType Environment { get; set; } = EnvironmentType.None;
    public ClientFlagContext Context { get; set; } = null!;
    public int Limit { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public List<FlagStepInfo>? Flags { get; set; }

    internal static ChallengeDetailModel FromInstance(GameInstance gameInstance, int attemptCount,
        ChallengeInfo? scoreboardChallenge = null)
    {
        var flags = gameInstance.Challenge.Flags;
        List<FlagStepInfo>? flagSteps = null;
        if (flags is { Count: > 1 })
        {
            flagSteps = flags
                .OrderBy(f => f.OrderIndex)
                .Select(f => new FlagStepInfo
                {
                    Id = f.Id,
                    OrderIndex = f.OrderIndex,
                    Description = f.Description,
                })
                .ToList();
        }

        return new()
        {
            Id = gameInstance.Challenge.Id,
            Content = gameInstance.Challenge.Content,
            Hints = gameInstance.Challenge.Hints,
            Score = scoreboardChallenge?.Score ?? gameInstance.Challenge.CurrentScore,
            Category = gameInstance.Challenge.Category,
            Title = gameInstance.Challenge.Title,
            Type = gameInstance.Challenge.Type,
            Environment = gameInstance.Challenge.Environment,
            Limit = gameInstance.Challenge.SubmissionLimit,
            Deadline = gameInstance.Challenge.DeadlineUtc,
            Attempts = attemptCount,
            Flags = flagSteps,
            Context = ClientFlagContext.FromInstance(
                gameInstance.Container,
                gameInstance.AttachmentUrl,
                gameInstance.Attachment?.FileSize)
        };
    }
}
