using GZCTF.Models.Request.Game;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class TheoryExamService(AppDbContext context)
{
    public const string DefaultBankName = "Default";

    private static readonly List<string> TrueFalseOptions = ["True", "False"];

    public static bool IsTheoryGame(Game game) => game.GameType is GameType.Theory or GameType.Mixed;

    public string? NormalizeAndValidate(TheoryQuestionEditModel model, int? score = null)
    {
        model.BankName = string.IsNullOrWhiteSpace(model.BankName) ? DefaultBankName : model.BankName.Trim();
        model.Title = model.Title.Trim();
        model.Content = model.Content.Trim();
        model.Options = model.Type == TheoryQuestionType.TrueFalse
            ? NormalizeTrueFalseOptions(model.Options)
            : model.Options.Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
        model.AnswerIndexes = NormalizeIndexes(model.AnswerIndexes);

        if (string.IsNullOrWhiteSpace(model.Title))
            return "Question title is required.";

        if (model.BankName.Length > 128)
            return "Question bank name is too long.";

        if (score is <= 0)
            return "Question score must be greater than 0.";

        if (model.Type is TheoryQuestionType.SingleChoice or TheoryQuestionType.MultipleChoice &&
            model.Options.Count < 2)
            return "Choice question requires at least two options.";

        if (model.Type is TheoryQuestionType.TrueFalse && model.Options.Count != 2)
            return "True/false question requires exactly two options.";

        if (model.AnswerIndexes.Count == 0)
            return "Correct answer is required.";

        if (model.AnswerIndexes.Any(i => i < 0 || i >= model.Options.Count))
            return "Correct answer index is out of range.";

        if (model.Type is TheoryQuestionType.SingleChoice or TheoryQuestionType.TrueFalse &&
            model.AnswerIndexes.Count != 1)
            return "Single choice and true/false questions require exactly one correct answer.";

        return null;
    }

    public string? ValidateAnswer(TheoryPaperQuestion question, List<int> selectedIndexes)
    {
        var normalized = NormalizeIndexes(selectedIndexes);
        if (normalized.Any(i => i < 0 || i >= question.Options.Count))
            return $"Answer index is out of range for question {question.Id}.";

        if (question.Type is TheoryQuestionType.SingleChoice or TheoryQuestionType.TrueFalse && normalized.Count > 1)
            return $"Question {question.Id} accepts only one answer.";

        return null;
    }

    public TheoryPaperQuestion ToPaperQuestion(TheoryPaperQuestionEditModel model, int index) =>
        new()
        {
            SourceQuestionId = model.SourceQuestionId,
            Type = model.Type,
            Title = model.Title.Trim(),
            Content = model.Content.Trim(),
            Options = model.Options,
            AnswerIndexes = NormalizeIndexes(model.AnswerIndexes),
            Score = model.Score,
            Order = model.Order > 0 ? model.Order : index + 1
        };

    public TheoryQuestionBankItem ToBankQuestion(TheoryQuestionEditModel model, TheoryQuestionBankItem? item = null)
    {
        item ??= new TheoryQuestionBankItem { CreatedAt = DateTimeOffset.UtcNow };
        item.Type = model.Type;
        item.BankName = string.IsNullOrWhiteSpace(model.BankName) ? DefaultBankName : model.BankName.Trim();
        item.Title = model.Title.Trim();
        item.Content = model.Content.Trim();
        item.Options = model.Options;
        item.AnswerIndexes = NormalizeIndexes(model.AnswerIndexes);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        return item;
    }

    public async Task<TheoryPaper?> GetPaper(int gameId, CancellationToken token) =>
        await context.TheoryPapers
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.GameId == gameId, token);

    public string? ApplyAnswers(TheoryAnswerSheet sheet, TheoryPaper paper, List<TheoryAnswerModel> answers)
    {
        var questions = paper.Questions.ToDictionary(q => q.Id);
        var incoming = answers
            .GroupBy(a => a.PaperQuestionId)
            .ToDictionary(g => g.Key, g => NormalizeIndexes(g.Last().SelectedIndexes));

        foreach (var (questionId, selected) in incoming)
        {
            if (!questions.TryGetValue(questionId, out var question))
                return $"Question {questionId} does not belong to this paper.";

            if (ValidateAnswer(question, selected) is { } error)
                return error;
        }

        sheet.Answers.RemoveAll(a => !incoming.ContainsKey(a.PaperQuestionId));

        foreach (var question in questions.Values)
        {
            var selected = incoming.GetValueOrDefault(question.Id, []);
            var answer = sheet.Answers.FirstOrDefault(a => a.PaperQuestionId == question.Id);
            if (answer is null)
            {
                answer = new TheorySubmissionAnswer { PaperQuestionId = question.Id };
                sheet.Answers.Add(answer);
            }

            answer.SelectedIndexes = selected;
            answer.IsCorrect = null;
            answer.Score = 0;
        }

        sheet.UpdatedAt = DateTimeOffset.UtcNow;
        return null;
    }

    public void Grade(TheoryAnswerSheet sheet, TheoryPaper paper)
    {
        var answers = sheet.Answers.ToDictionary(a => a.PaperQuestionId);
        var score = 0;

        foreach (var question in paper.Questions)
        {
            if (!answers.TryGetValue(question.Id, out var answer))
                continue;

            var selected = NormalizeIndexes(answer.SelectedIndexes);
            var correct = selected.SequenceEqual(NormalizeIndexes(question.AnswerIndexes));
            answer.SelectedIndexes = selected;
            answer.IsCorrect = correct;
            answer.Score = correct ? question.Score : 0;
            score += answer.Score;
        }

        sheet.Score = score;
        sheet.MaxScore = paper.Questions.Sum(q => q.Score);
        sheet.Status = TheoryAnswerSheetStatus.Submitted;
        sheet.SubmittedAt = DateTimeOffset.UtcNow;
        sheet.UpdatedAt = sheet.SubmittedAt.Value;
    }

    public async Task<TheoryResultsModel> BuildResults(int gameId, CancellationToken token)
    {
        var paper = await GetPaper(gameId, token);
        var maxScore = paper?.Questions.Sum(q => q.Score) ?? 0;

        var participations = await context.Participations
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .Include(p => p.Team)
            .OrderBy(p => p.TeamId)
            .ToListAsync(token);

        var sheets = await context.TheoryAnswerSheets
            .Where(s => s.GameId == gameId)
            .Include(s => s.Participation)
            .ThenInclude(p => p.Team)
            .Include(s => s.User)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(token);

        var submittedSheets = sheets
            .Where(s => s.Status == TheoryAnswerSheetStatus.Submitted)
            .GroupBy(s => s.ParticipationId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Score)
                    .ThenBy(s => s.SubmittedAt ?? DateTimeOffset.MaxValue)
                    .First());

        var scoreboard = participations
            .Select(part =>
            {
                submittedSheets.TryGetValue(part.Id, out var best);
                return new TheoryScoreboardItemModel
                {
                    TeamId = part.TeamId,
                    TeamName = part.Team?.Name ?? string.Empty,
                    DivisionId = part.DivisionId,
                    Score = best?.Score ?? 0,
                    MaxScore = best?.MaxScore ?? maxScore,
                    UserName = best?.User.UserName,
                    SubmittedAt = best?.SubmittedAt
                };
            })
            .OrderByDescending(i => i.Score)
            .ThenBy(i => i.SubmittedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.TeamId)
            .ToList();

        for (var i = 0; i < scoreboard.Count; i++)
            scoreboard[i].Rank = i + 1;

        return new TheoryResultsModel
        {
            Submissions = sheets.Select(TheoryAnswerSheetSummaryModel.FromSheet).ToList(),
            Scoreboard = scoreboard
        };
    }

    public async Task RegradeSubmittedSheets(int gameId, CancellationToken token)
    {
        var paper = await context.TheoryPapers
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.GameId == gameId, token);

        if (paper is null)
            return;

        var sheets = await context.TheoryAnswerSheets
            .Where(s => s.GameId == gameId && s.Status == TheoryAnswerSheetStatus.Submitted)
            .Include(s => s.Answers)
            .ToListAsync(token);

        foreach (var sheet in sheets)
            Grade(sheet, paper);

        await context.SaveChangesAsync(token);
    }

    public static List<int> NormalizeIndexes(IEnumerable<int> indexes) =>
        indexes.Distinct().OrderBy(i => i).ToList();

    private static List<string> NormalizeTrueFalseOptions(List<string> options)
    {
        var normalized = options.Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
        return normalized.Count == 0 ? [.. TrueFalseOptions] : normalized;
    }
}
