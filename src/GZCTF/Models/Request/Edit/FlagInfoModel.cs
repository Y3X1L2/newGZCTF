namespace GZCTF.Models.Request.Edit;

public class FlagInfoModel
{
    public int Id { get; set; }

    public string Flag { get; set; } = string.Empty;

    public int OrderIndex { get; set; }

    public string? Description { get; set; }

    public FlagScoreMode ScoreMode { get; set; }

    public int FixedScore { get; set; }

    public int MaxAttempts { get; set; }

    public AnswerType AnswerType { get; set; }

    public string? CustomName { get; set; }

    public string? AttachmentHash { get; set; }

    public Attachment? Attachment { get; set; }

    internal static FlagInfoModel FromFlagContext(FlagContext context) =>
        new()
        {
            Id = context.Id,
            Flag = context.Flag,
            OrderIndex = context.OrderIndex,
            Description = context.Description,
            ScoreMode = context.ScoreMode,
            FixedScore = context.FixedScore,
            MaxAttempts = context.MaxAttempts,
            AnswerType = context.AnswerType,
            CustomName = context.CustomName,
            AttachmentHash = context.AttachmentHash,
            Attachment = context.Attachment
        };
}
