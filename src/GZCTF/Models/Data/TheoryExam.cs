using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GZCTF.Modules.Theory.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

public class TheoryQuestionBankItem
{
    [Key]
    public int Id { get; set; }

    public TheoryQuestionType Type { get; set; }

    [Required]
    public string BankName { get; set; } = "Default";

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public List<int> AnswerIndexes { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public ICollection<TheoryQuestionTagBinding> TagBindings { get; set; } = [];
}

public class TheoryPaper
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int TotalScore => Questions.Sum(q => q.Score);

    public int GameId { get; set; }

    [JsonIgnore]
    public Game Game { get; set; } = null!;

    public List<TheoryPaperQuestion> Questions { get; set; } = [];
}

public class TheoryPaperQuestion
{
    [Key]
    public int Id { get; set; }

    public int PaperId { get; set; }

    [JsonIgnore]
    public TheoryPaper Paper { get; set; } = null!;

    public int? SourceQuestionId { get; set; }

    [JsonIgnore]
    public TheoryQuestionBankItem? SourceQuestion { get; set; }

    public TheoryQuestionType Type { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public List<int> AnswerIndexes { get; set; } = [];

    public int Score { get; set; } = 1;

    public int Order { get; set; }
}

public class TheoryAnswerSheet
{
    [Key]
    public int Id { get; set; }

    public TheoryAnswerSheetStatus Status { get; set; } = TheoryAnswerSheetStatus.Draft;

    public int Score { get; set; }

    public int MaxScore { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SubmittedAt { get; set; }

    public int GameId { get; set; }

    [JsonIgnore]
    public Game Game { get; set; } = null!;

    public int PaperId { get; set; }

    [JsonIgnore]
    public TheoryPaper Paper { get; set; } = null!;

    public int ParticipationId { get; set; }

    [JsonIgnore]
    public Participation Participation { get; set; } = null!;

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public List<TheorySubmissionAnswer> Answers { get; set; } = [];
}

public class TheorySubmissionAnswer
{
    [Key]
    public int Id { get; set; }

    public int AnswerSheetId { get; set; }

    [JsonIgnore]
    public TheoryAnswerSheet AnswerSheet { get; set; } = null!;

    public int PaperQuestionId { get; set; }

    [JsonIgnore]
    public TheoryPaperQuestion PaperQuestion { get; set; } = null!;

    public List<int> SelectedIndexes { get; set; } = [];

    public bool? IsCorrect { get; set; }

    public int Score { get; set; }
}
