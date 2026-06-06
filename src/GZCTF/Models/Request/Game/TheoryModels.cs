using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Game;

public class TheoryQuestionEditModel
{
    [Required]
    public TheoryQuestionType Type { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public List<int> AnswerIndexes { get; set; } = [];
}

public class TheoryQuestionBankItemModel : TheoryQuestionEditModel
{
    public int Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    internal static TheoryQuestionBankItemModel FromEntity(TheoryQuestionBankItem item) =>
        new()
        {
            Id = item.Id,
            Type = item.Type,
            Title = item.Title,
            Content = item.Content,
            Options = item.Options,
            AnswerIndexes = item.AnswerIndexes,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
}

public class TheoryPaperQuestionEditModel : TheoryQuestionEditModel
{
    public int Id { get; set; }

    public int? SourceQuestionId { get; set; }

    [Range(1, int.MaxValue)]
    public int Score { get; set; } = 1;

    public int Order { get; set; }
}

public class TheoryPaperEditModel
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<TheoryPaperQuestionEditModel> Questions { get; set; } = [];
}

public class TheoryPaperDetailModel : TheoryPaperEditModel
{
    public int Id { get; set; }

    public int GameId { get; set; }

    public bool IsPublished { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int TotalScore { get; set; }

    internal static TheoryPaperDetailModel Empty(GZCTF.Models.Data.Game game) =>
        new()
        {
            GameId = game.Id,
            Title = string.IsNullOrWhiteSpace(game.Title) ? "Theory Paper" : game.Title,
            Description = string.Empty,
            Questions = [],
            TotalScore = 0
        };

    internal static TheoryPaperDetailModel FromPaper(TheoryPaper paper) =>
        new()
        {
            Id = paper.Id,
            GameId = paper.GameId,
            Title = paper.Title,
            Description = paper.Description,
            IsPublished = paper.IsPublished,
            PublishedAt = paper.PublishedAt,
            UpdatedAt = paper.UpdatedAt,
            TotalScore = paper.Questions.Sum(q => q.Score),
            Questions = paper.Questions
                .OrderBy(q => q.Order)
                .Select(q => new TheoryPaperQuestionEditModel
                {
                    Id = q.Id,
                    SourceQuestionId = q.SourceQuestionId,
                    Type = q.Type,
                    Title = q.Title,
                    Content = q.Content,
                    Options = q.Options,
                    AnswerIndexes = q.AnswerIndexes,
                    Score = q.Score,
                    Order = q.Order
                })
                .ToList()
        };
}

public class TheoryAnswerModel
{
    public int PaperQuestionId { get; set; }

    public List<int> SelectedIndexes { get; set; } = [];
}

public class TheoryAnswerSheetEditModel
{
    public List<TheoryAnswerModel> Answers { get; set; } = [];
}

public class TheoryPlayerQuestionModel
{
    public int Id { get; set; }

    public TheoryQuestionType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public int Score { get; set; }

    public int Order { get; set; }

    internal static TheoryPlayerQuestionModel FromQuestion(TheoryPaperQuestion question) =>
        new()
        {
            Id = question.Id,
            Type = question.Type,
            Title = question.Title,
            Content = question.Content,
            Options = question.Options,
            Score = question.Score,
            Order = question.Order
        };
}

public class TheoryPlayerPaperModel
{
    public int PaperId { get; set; }

    public int GameId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int TotalScore { get; set; }

    public TheoryAnswerSheetStatus? Status { get; set; }

    public int? Score { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<TheoryPlayerQuestionModel> Questions { get; set; } = [];

    public List<TheoryAnswerModel> Answers { get; set; } = [];

    internal static TheoryPlayerPaperModel FromPaper(TheoryPaper paper, TheoryAnswerSheet? sheet) =>
        new()
        {
            PaperId = paper.Id,
            GameId = paper.GameId,
            Title = paper.Title,
            Description = paper.Description,
            TotalScore = paper.Questions.Sum(q => q.Score),
            Status = sheet?.Status,
            Score = sheet?.Status == TheoryAnswerSheetStatus.Submitted ? sheet.Score : null,
            SubmittedAt = sheet?.SubmittedAt,
            UpdatedAt = sheet?.UpdatedAt,
            Questions = paper.Questions
                .OrderBy(q => q.Order)
                .Select(TheoryPlayerQuestionModel.FromQuestion)
                .ToList(),
            Answers = sheet?.Answers
                .Select(a => new TheoryAnswerModel
                {
                    PaperQuestionId = a.PaperQuestionId,
                    SelectedIndexes = a.SelectedIndexes
                })
                .ToList() ?? []
        };
}

public class TheoryAnswerSheetSummaryModel
{
    public int Id { get; set; }

    public int ParticipationId { get; set; }

    public int TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public TheoryAnswerSheetStatus Status { get; set; }

    public int Score { get; set; }

    public int MaxScore { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    internal static TheoryAnswerSheetSummaryModel FromSheet(TheoryAnswerSheet sheet) =>
        new()
        {
            Id = sheet.Id,
            ParticipationId = sheet.ParticipationId,
            TeamId = sheet.Participation.TeamId,
            TeamName = sheet.Participation.Team?.Name ?? string.Empty,
            UserId = sheet.UserId,
            UserName = sheet.User.UserName ?? string.Empty,
            Status = sheet.Status,
            Score = sheet.Score,
            MaxScore = sheet.MaxScore,
            UpdatedAt = sheet.UpdatedAt,
            SubmittedAt = sheet.SubmittedAt
        };
}

public class TheoryScoreboardItemModel
{
    public int Rank { get; set; }

    public int TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public int? DivisionId { get; set; }

    public int Score { get; set; }

    public int MaxScore { get; set; }

    public string? UserName { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }
}

public class TheoryResultsModel
{
    public List<TheoryAnswerSheetSummaryModel> Submissions { get; set; } = [];

    public List<TheoryScoreboardItemModel> Scoreboard { get; set; } = [];
}
