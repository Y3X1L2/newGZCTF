using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[RequireTeacher]
[ApiController]
[Route("api/admin/theory")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class TheoryAdminController(
    AppDbContext context,
    IGameRepository gameRepository,
    TheoryExamService theoryService) : ControllerBase
{
    [HttpGet("questions")]
    [ProducesResponseType(typeof(TheoryQuestionBankItemModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuestions(
        [FromQuery] string? keyword,
        [FromQuery][Range(0, 5000)] int count = 1000,
        [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var query = context.TheoryQuestionBankItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(q => q.Title.Contains(keyword) || q.Content.Contains(keyword));

        query = query.OrderByDescending(q => q.UpdatedAt);

        if (count > 0)
            query = query.Skip(skip).Take(count);

        var items = await query.ToArrayAsync(token);

        return Ok(items.Select(TheoryQuestionBankItemModel.FromEntity).ToArray());
    }

    [HttpPost("questions")]
    [ProducesResponseType(typeof(TheoryQuestionBankItemModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateQuestion([FromBody] TheoryQuestionEditModel model, CancellationToken token)
    {
        if (theoryService.NormalizeAndValidate(model) is { } error)
            return BadRequest(new RequestResponse(error));

        var item = theoryService.ToBankQuestion(model);
        context.TheoryQuestionBankItems.Add(item);
        await context.SaveChangesAsync(token);

        return Ok(TheoryQuestionBankItemModel.FromEntity(item));
    }

    [HttpPut("questions/{id:int}")]
    [ProducesResponseType(typeof(TheoryQuestionBankItemModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateQuestion(
        [FromRoute] int id,
        [FromBody] TheoryQuestionEditModel model,
        CancellationToken token)
    {
        if (theoryService.NormalizeAndValidate(model) is { } error)
            return BadRequest(new RequestResponse(error));

        var item = await context.TheoryQuestionBankItems.FirstOrDefaultAsync(q => q.Id == id, token);
        if (item is null)
            return NotFound(new RequestResponse("Question not found.", StatusCodes.Status404NotFound));

        theoryService.ToBankQuestion(model, item);
        await context.SaveChangesAsync(token);

        return Ok(TheoryQuestionBankItemModel.FromEntity(item));
    }

    [HttpDelete("questions/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteQuestion([FromRoute] int id, CancellationToken token)
    {
        var item = await context.TheoryQuestionBankItems.FirstOrDefaultAsync(q => q.Id == id, token);
        if (item is null)
            return NotFound(new RequestResponse("Question not found.", StatusCodes.Status404NotFound));

        context.TheoryQuestionBankItems.Remove(item);
        await context.SaveChangesAsync(token);

        return NoContent();
    }

    [HttpGet("games/{gameId:int}/paper")]
    [ProducesResponseType(typeof(TheoryPaperDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaper([FromRoute] int gameId, CancellationToken token)
    {
        var (game, error) = await GetTheoryGame(gameId, token);
        if (error is not null)
            return error;

        var paper = await theoryService.GetPaper(gameId, token);
        return Ok(paper is null ? TheoryPaperDetailModel.Empty(game!) : TheoryPaperDetailModel.FromPaper(paper));
    }

    [HttpPut("games/{gameId:int}/paper")]
    [ProducesResponseType(typeof(TheoryPaperDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SavePaper(
        [FromRoute] int gameId,
        [FromBody] TheoryPaperEditModel model,
        CancellationToken token)
    {
        var (game, error) = await GetTheoryGame(gameId, token);
        if (error is not null)
            return error;

        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(new RequestResponse("Paper title is required."));

        if (model.Questions.Count == 0)
            return BadRequest(new RequestResponse("Paper requires at least one question."));

        for (var i = 0; i < model.Questions.Count; i++)
        {
            var question = model.Questions[i];
            if (theoryService.NormalizeAndValidate(question, question.Score) is { } validateError)
                return BadRequest(new RequestResponse($"Question {i + 1}: {validateError}"));
        }

        var sourceIds = model.Questions
            .Where(q => q.SourceQuestionId.HasValue)
            .Select(q => q.SourceQuestionId!.Value)
            .Distinct()
            .ToArray();

        if (sourceIds.Length > 0)
        {
            var existingIds = await context.TheoryQuestionBankItems
                .Where(q => sourceIds.Contains(q.Id))
                .Select(q => q.Id)
                .ToListAsync(token);

            if (sourceIds.Except(existingIds).Any())
                return BadRequest(new RequestResponse("Some source questions do not exist."));
        }

        var paper = await context.TheoryPapers
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.GameId == gameId, token);

        if (paper is not null &&
            await context.TheoryAnswerSheets.AnyAsync(s =>
                s.PaperId == paper.Id && s.Status == TheoryAnswerSheetStatus.Submitted, token))
            return BadRequest(new RequestResponse("Published paper with submissions cannot be edited."));

        paper ??= new TheoryPaper
        {
            GameId = game!.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var oldQuestions = paper.Questions.ToList();
        context.TheoryPaperQuestions.RemoveRange(oldQuestions);
        paper.Questions.Clear();

        paper.Title = model.Title.Trim();
        paper.Description = model.Description.Trim();
        paper.IsPublished = false;
        paper.PublishedAt = null;
        paper.UpdatedAt = DateTimeOffset.UtcNow;

        for (var i = 0; i < model.Questions.Count; i++)
            paper.Questions.Add(theoryService.ToPaperQuestion(model.Questions[i], i));

        if (paper.Id == 0)
            context.TheoryPapers.Add(paper);

        await context.SaveChangesAsync(token);

        return Ok(TheoryPaperDetailModel.FromPaper(paper));
    }

    [HttpPost("games/{gameId:int}/paper/publish")]
    [ProducesResponseType(typeof(TheoryPaperDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PublishPaper([FromRoute] int gameId, CancellationToken token)
    {
        var (_, error) = await GetTheoryGame(gameId, token);
        if (error is not null)
            return error;

        var paper = await context.TheoryPapers
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.GameId == gameId, token);

        if (paper is null || paper.Questions.Count == 0)
            return BadRequest(new RequestResponse("Paper is empty."));

        paper.IsPublished = true;
        paper.PublishedAt ??= DateTimeOffset.UtcNow;
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        return Ok(TheoryPaperDetailModel.FromPaper(paper));
    }

    [HttpGet("games/{gameId:int}/results")]
    [ProducesResponseType(typeof(TheoryResultsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetResults([FromRoute] int gameId, CancellationToken token)
    {
        var (_, error) = await GetTheoryGame(gameId, token);
        if (error is not null)
            return error;

        return Ok(await theoryService.BuildResults(gameId, token));
    }

    [HttpPost("games/{gameId:int}/results/recalculate")]
    [ProducesResponseType(typeof(TheoryResultsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecalculateResults([FromRoute] int gameId, CancellationToken token)
    {
        var (_, error) = await GetTheoryGame(gameId, token);
        if (error is not null)
            return error;

        await theoryService.RegradeSubmittedSheets(gameId, token);
        return Ok(await theoryService.BuildResults(gameId, token));
    }

    private async Task<(Game? Game, IActionResult? Error)> GetTheoryGame(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return (null, NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound)));

        if (!TheoryExamService.IsTheoryGame(game))
            return (null, BadRequest(new RequestResponse("This game is not a theory exam.")));

        return (game, null);
    }
}
