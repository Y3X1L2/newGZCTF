using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/theory")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class TheoryPlayerController(
    AppDbContext context,
    IGameRepository gameRepository,
    IParticipationRepository participationRepository,
    UserManager<UserInfo> userManager,
    TheoryExamService theoryService) : ControllerBase
{
    [RequireUser]
    [HttpGet("games/{gameId:int}/paper")]
    [ProducesResponseType(typeof(TheoryPlayerPaperModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaper([FromRoute] int gameId, CancellationToken token)
    {
        var (game, participation, user, error) = await GetPlayerContext(gameId, token);
        if (error is not null)
            return error;

        var paper = await GetPublishedPaper(game!.Id, token);
        if (paper is null)
            return NotFound(new RequestResponse("Theory paper is not published.", StatusCodes.Status404NotFound));

        var sheet = await context.TheoryAnswerSheets
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s =>
                s.GameId == game.Id &&
                s.PaperId == paper.Id &&
                s.UserId == user!.Id &&
                s.ParticipationId == participation!.Id, token);

        return Ok(TheoryPlayerPaperModel.FromPaper(paper, sheet));
    }

    [RequireUser]
    [HttpPut("games/{gameId:int}/draft")]
    [ProducesResponseType(typeof(TheoryPlayerPaperModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveDraft(
        [FromRoute] int gameId,
        [FromBody] TheoryAnswerSheetEditModel model,
        CancellationToken token)
    {
        var (game, participation, user, error) = await GetPlayerContext(gameId, token);
        if (error is not null)
            return error;

        var paper = await GetPublishedPaper(game!.Id, token);
        if (paper is null)
            return NotFound(new RequestResponse("Theory paper is not published.", StatusCodes.Status404NotFound));

        var sheet = await GetOrCreateSheet(game, paper, participation!, user!, token);
        if (sheet.Status == TheoryAnswerSheetStatus.Submitted)
            return BadRequest(new RequestResponse("Answer sheet has already been submitted."));

        if (theoryService.ApplyAnswers(sheet, paper, model.Answers) is { } applyError)
            return BadRequest(new RequestResponse(applyError));

        sheet.Status = TheoryAnswerSheetStatus.Draft;
        sheet.MaxScore = paper.Questions.Sum(q => q.Score);
        await context.SaveChangesAsync(token);

        return Ok(TheoryPlayerPaperModel.FromPaper(paper, sheet));
    }

    [RequireUser]
    [HttpPost("games/{gameId:int}/submit")]
    [ProducesResponseType(typeof(TheoryPlayerPaperModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submit(
        [FromRoute] int gameId,
        [FromBody] TheoryAnswerSheetEditModel model,
        CancellationToken token)
    {
        var (game, participation, user, error) = await GetPlayerContext(gameId, token);
        if (error is not null)
            return error;

        var paper = await GetPublishedPaper(game!.Id, token);
        if (paper is null)
            return NotFound(new RequestResponse("Theory paper is not published.", StatusCodes.Status404NotFound));

        await using var transaction = await context.Database.BeginTransactionAsync(token);

        var sheet = await GetOrCreateSheet(game, paper, participation!, user!, token);
        if (sheet.Status == TheoryAnswerSheetStatus.Submitted)
            return BadRequest(new RequestResponse("Answer sheet has already been submitted."));

        if (theoryService.ApplyAnswers(sheet, paper, model.Answers) is { } applyError)
            return BadRequest(new RequestResponse(applyError));

        theoryService.Grade(sheet, paper);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);

        return Ok(TheoryPlayerPaperModel.FromPaper(paper, sheet));
    }

    [HttpGet("games/{gameId:int}/scoreboard")]
    [ProducesResponseType(typeof(TheoryScoreboardItemModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Scoreboard([FromRoute] int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound));

        if (!TheoryExamService.IsTheoryGame(game))
            return BadRequest(new RequestResponse("This game is not a theory exam."));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse("Game has not started."));

        var results = await theoryService.BuildResults(gameId, token);
        return Ok(results.Scoreboard.ToArray());
    }

    private async Task<TheoryPaper?> GetPublishedPaper(int gameId, CancellationToken token) =>
        await context.TheoryPapers
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.GameId == gameId && p.IsPublished, token);

    private async Task<TheoryAnswerSheet> GetOrCreateSheet(
        Game game,
        TheoryPaper paper,
        Participation participation,
        UserInfo user,
        CancellationToken token)
    {
        var sheet = await context.TheoryAnswerSheets
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s =>
                s.GameId == game.Id &&
                s.PaperId == paper.Id &&
                s.UserId == user.Id &&
                s.ParticipationId == participation.Id, token);

        if (sheet is not null)
            return sheet;

        sheet = new TheoryAnswerSheet
        {
            GameId = game.Id,
            PaperId = paper.Id,
            ParticipationId = participation.Id,
            UserId = user.Id,
            Status = TheoryAnswerSheetStatus.Draft,
            MaxScore = paper.Questions.Sum(q => q.Score),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.TheoryAnswerSheets.Add(sheet);
        return sheet;
    }

    private async Task<(Game? Game, Participation? Participation, UserInfo? User, IActionResult? Error)>
        GetPlayerContext(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return (null, null, null, NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound)));

        if (!TheoryExamService.IsTheoryGame(game))
            return (null, null, null, BadRequest(new RequestResponse("This game is not a theory exam.")));

        var now = DateTimeOffset.UtcNow;
        if (now < game.StartTimeUtc)
            return (null, null, null, BadRequest(new RequestResponse("Game has not started.")));

        if (now > game.EndTimeUtc && !game.PracticeMode)
            return (null, null, null, BadRequest(new RequestResponse("Game has ended.")));

        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return (null, null, null, Unauthorized(new RequestResponse("Login is required.", StatusCodes.Status401Unauthorized)));

        var participation = await participationRepository.GetParticipation(user.Id, game.Id, token);
        if (participation is null || participation.Status != ParticipationStatus.Accepted)
            return (null, null, null, StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse("Accepted participation is required.", StatusCodes.Status403Forbidden)));

        return (game, participation, user, null);
    }
}
