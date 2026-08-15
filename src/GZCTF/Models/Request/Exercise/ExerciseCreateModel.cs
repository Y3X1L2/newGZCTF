using GZCTF.Models.Request.Edit;

namespace GZCTF.Models.Request.Exercise;

public class ExerciseCreateModel
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;
    public Difficulty Difficulty { get; set; } = Difficulty.Baby;
    public bool Credit { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<string>? Tags { get; set; }
    public List<string>? Hints { get; set; }
    public string? ContainerImage { get; set; }
    public int? MemoryLimit { get; set; } = 64;
    public int? StorageLimit { get; set; } = 256;
    public int? CPUCount { get; set; } = 1;
    public int? ExposePort { get; set; } = 80;
    public NetworkMode? NetworkMode { get; set; } = Utils.NetworkMode.Open;
    public EnvironmentType Environment { get; set; } = EnvironmentType.None;
    public int? ImageTemplateId { get; set; }
    public string? FlagTemplate { get; set; }
    public int SubmissionLimit { get; set; }
    public List<ExerciseFlagCreateModel>? Flags { get; set; }
    public AttachmentCreateModel? Attachment { get; set; }
}

public class ExerciseFlagCreateModel : FlagCreateModel
{
    public int? Id { get; set; }
}

public class ExerciseManagementModel : ExerciseCreateModel
{
    public int Id { get; set; }
    public string? CreatorUserName { get; set; }

    internal static ExerciseManagementModel FromExercise(ExerciseChallenge exercise) => new()
    {
        Id = exercise.Id,
        CreatorUserName = exercise.CreatedBy?.UserName,
        Title = exercise.Title,
        Content = exercise.Content,
        Category = exercise.Category,
        Type = exercise.Type,
        Difficulty = exercise.Difficulty,
        Credit = exercise.Credit,
        IsEnabled = exercise.IsEnabled,
        Tags = exercise.Tags,
        Hints = exercise.Hints,
        ContainerImage = exercise.ContainerImage,
        MemoryLimit = exercise.MemoryLimit,
        StorageLimit = exercise.StorageLimit,
        CPUCount = exercise.CPUCount,
        ExposePort = exercise.ExposePort,
        NetworkMode = exercise.NetworkMode,
        Environment = exercise.Environment,
        ImageTemplateId = exercise.ImageTemplateId,
        FlagTemplate = exercise.FlagTemplate,
        SubmissionLimit = exercise.SubmissionLimit,
        Attachment = ToAttachmentModel(exercise.Attachment),
        Flags = exercise.Flags.OrderBy(flag => flag.OrderIndex).Select(flag => new ExerciseFlagCreateModel
        {
            Id = flag.Id,
            Flag = flag.Flag,
            OrderIndex = flag.OrderIndex,
            Description = flag.Description,
            ScoreMode = flag.ScoreMode,
            FixedScore = flag.FixedScore,
            MaxAttempts = flag.MaxAttempts,
            AttachmentHash = flag.AttachmentHash,
            AnswerType = flag.AnswerType,
            CustomName = flag.CustomName,
            AttachmentType = flag.Attachment?.Type ?? FileType.None,
            FileHash = flag.Attachment?.LocalFile?.Hash,
            RemoteUrl = flag.Attachment?.RemoteUrl
        }).ToList()
    };

    static AttachmentCreateModel? ToAttachmentModel(Attachment? attachment) => attachment is null
        ? null
        : new AttachmentCreateModel
        {
            AttachmentType = attachment.Type,
            FileHash = attachment.LocalFile?.Hash,
            RemoteUrl = attachment.RemoteUrl
        };
}
