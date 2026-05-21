using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public class ChallengeSubmissionType
{
    [Key]
    public int Id { get; set; }

    public int ChallengeId { get; set; }

    public AnswerType Type { get; set; } = AnswerType.Flag;

    public int OrderIndex { get; set; }

    [MaxLength(64)]
    public string? Label { get; set; }

    public bool IsActive { get; set; } = true;

    public GameChallenge? Challenge { get; set; }
}
