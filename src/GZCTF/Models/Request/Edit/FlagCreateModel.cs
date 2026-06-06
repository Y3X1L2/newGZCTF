using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Edit;

/// <summary>
/// New Flag information (Edit)
/// </summary>
public class FlagCreateModel
{
    /// <summary>
    /// Flag text
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_FlagRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxFlagLength, ErrorMessageResourceName = nameof(Resources.Program.Model_FlagTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Flag { get; set; } = string.Empty;

    /// <summary>
    /// Display order index for this flag in the challenge
    /// </summary>
    public int OrderIndex { get; set; }

    /// <summary>
    /// Description of this flag/checkpoint
    /// </summary>
    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>
    /// Score mode for this flag
    /// </summary>
    public FlagScoreMode ScoreMode { get; set; } = FlagScoreMode.InheritDecay;

    /// <summary>
    /// Fixed score value (used when ScoreMode is Fixed)
    /// </summary>
    public int FixedScore { get; set; }

    /// <summary>
    /// Maximum number of submission attempts for this flag
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    /// SHA256 hash of the attachment file
    /// </summary>
    [MaxLength(128)]
    public string? AttachmentHash { get; set; }

    /// <summary>
    /// Type of answer expected
    /// </summary>
    public AnswerType AnswerType { get; set; } = AnswerType.Flag;

    /// <summary>
    /// Custom display name for this flag
    /// </summary>
    [MaxLength(64)]
    public string? CustomName { get; set; }

    /// <summary>
    /// Attachment type
    /// </summary>
    public FileType AttachmentType { get; set; } = FileType.None;

    /// <summary>
    /// File hash (local file)
    /// </summary>
    public string? FileHash { get; set; } = string.Empty;

    /// <summary>
    /// File URL (remote file)
    /// </summary>
    public string? RemoteUrl { get; set; } = string.Empty;

    internal Attachment? ToAttachment(LocalFile? localFile) => AttachmentType switch
    {
        FileType.None => null,
        _ => new() { Type = AttachmentType, LocalFile = localFile, RemoteUrl = RemoteUrl }
    };
}
