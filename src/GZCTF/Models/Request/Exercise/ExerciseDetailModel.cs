using GZCTF.Models.Request.Shared;
using GZCTF.Services.Fleet;

namespace GZCTF.Models.Request.Exercise;

public class ExerciseDetailModel
{
    /// <summary>
    /// Exercise ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Exercise title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Exercise content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Exercise category
    /// </summary>
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;

    /// <summary>
    /// Exercise hints
    /// </summary>
    public List<string>? Hints { get; set; }

    /// <summary>
    /// Exercise credits
    /// </summary>
    public bool Credit { get; set; }

    /// <summary>
    /// Difficulty of the exercise, used for tags, sorting, etc.
    /// </summary>
    public Difficulty Difficulty { get; set; }

    /// <summary>
    /// Additional tags for the exercise
    /// </summary>
    public List<string>? Tags { get; set; } = [];

    /// <summary>
    /// Exercise type
    /// </summary>
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;

    public ExercisePoolSource PoolSource { get; set; }

    /// <summary>
    /// Flag context
    /// </summary>
    public ClientFlagContext Context { get; set; } = null!;

    public ExerciseFlagInfoModel[] Flags { get; set; } = [];

    public int[] SolvedFlagIds { get; set; } = [];

    public int Attempts { get; set; }

    public int? Limit { get; set; }

    public bool Solved { get; set; }

    public DeploymentQueueStatusModel? Queue { get; set; }

    internal static ExerciseDetailModel FromInstance(
        ExerciseInstance instance,
        int attempts,
        int[] solvedFlagIds,
        DeploymentQueueStatusModel? queue) =>
        new()
        {
            Id = instance.ExerciseId,
            Content = instance.Exercise.Content,
            Hints = instance.Exercise.Hints,
            Credit = instance.Exercise.Credit,
            Difficulty = instance.Exercise.Difficulty,
            Category = instance.Exercise.Category,
            Tags = instance.Exercise.Tags,
            Title = instance.Exercise.Title,
            Type = instance.Exercise.Type,
            PoolSource = instance.Exercise.PoolSource,
            Attempts = attempts,
            Limit = instance.Exercise.SubmissionLimit > 0 ? instance.Exercise.SubmissionLimit : null,
            Solved = instance.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0),
            SolvedFlagIds = solvedFlagIds,
            Flags = VisibleFlags(instance).Select(ExerciseFlagInfoModel.FromFlag).ToArray(),
            Queue = queue,
            Context = ClientFlagContext.FromInstance(
                instance.Container,
                instance.AttachmentUrl,
                instance.Attachment?.FileSize)
        };

    static IEnumerable<FlagContext> VisibleFlags(ExerciseInstance instance) => instance.Exercise.Type switch
    {
        ChallengeType.DynamicAttachment or ChallengeType.DynamicContainer when instance.FlagContext is not null =>
            [instance.FlagContext],
        _ => instance.Exercise.Flags.OrderBy(flag => flag.OrderIndex)
    };
}

public class ExerciseFlagInfoModel
{
    public int Id { get; set; }
    public int OrderIndex { get; set; }
    public string? Description { get; set; }
    public string? CustomName { get; set; }
    public AnswerType AnswerType { get; set; }
    public string? AttachmentUrl { get; set; }
    public long? AttachmentFileSize { get; set; }

    internal static ExerciseFlagInfoModel FromFlag(FlagContext flag) => new()
    {
        Id = flag.Id,
        OrderIndex = flag.OrderIndex,
        Description = flag.Description,
        CustomName = flag.CustomName,
        AnswerType = flag.AnswerType,
        AttachmentUrl = flag.Attachment?.UrlWithName(),
        AttachmentFileSize = flag.Attachment?.FileSize
    };
}
