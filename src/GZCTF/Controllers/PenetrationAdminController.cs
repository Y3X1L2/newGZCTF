using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Modules.Penetration.Application;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

[RequireTeacher]
[ApiController]
[Route("api/admin/pentest")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class PenetrationAdminController(
    IGameRepository games,
    UserManager<UserInfo> users,
    PenetrationTeamLabAdapter adapter,
    PenetrationObjectiveService objectives) : ControllerBase
{
    [HttpGet("games/{gameId:int}/binding")]
    public async Task<IActionResult> GetBinding(int gameId, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        var binding = await adapter.GetBindingAsync(gameId, cancellationToken);
        return binding is null ? NotFound(new RequestResponse("The game has no TeamLab topology binding.")) : Ok(binding);
    }

    [HttpPut("games/{gameId:int}/binding")]
    public async Task<IActionResult> Bind(int gameId, BindPenetrationTopologyModel model, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return Ok(await adapter.BindAsync(gameId, model.TopologyId, cancellationToken));
    }

    [HttpPut("games/{gameId:int}/objectives")]
    public async Task<IActionResult> ReplaceObjectives(
        int gameId,
        ReplacePenetrationObjectivesModel model,
        CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return Ok(await objectives.ReplaceAsync(gameId, model, cancellationToken));
    }

    [HttpPost("games/{gameId:int}/releases/{releaseId:guid}/activate")]
    public async Task<IActionResult> ActivateRelease(int gameId, Guid releaseId, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return Ok(await adapter.ActivateReleaseAsync(gameId, releaseId, cancellationToken));
    }

    [HttpPost("games/{gameId:int}/deploy")]
    public async Task<IActionResult> Deploy(int gameId, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        var actor = await users.GetUserAsync(User);
        if (actor is null) return Unauthorized(new RequestResponse("Login required."));
        var result = await adapter.DeployGameAsync(gameId, actor.Id, cancellationToken);
        return Accepted(new { message = $"Queued {result.Created} team runtime(s); reused {result.Reused}." });
    }

    [HttpPost("games/{gameId:int}/stop")]
    public async Task<IActionResult> Stop(int gameId, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        await adapter.DestroyGameAsync(gameId, cancellationToken);
        return Ok(new RequestResponse("All TeamLab runtimes were destroyed.", StatusCodes.Status200OK));
    }

    [HttpPost("games/{gameId:int}/teams/{teamId:int}/rebuild")]
    public async Task<IActionResult> RebuildTeam(int gameId, int teamId, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        var actor = await users.GetUserAsync(User);
        if (actor is null) return Unauthorized(new RequestResponse("Login required."));
        var result = await adapter.ResetTeamAsync(gameId, teamId, actor.Id, true, cancellationToken);
        return Accepted(new { runtimeId = result.RuntimePublicId, result.Reused });
    }

    [HttpPost("games/{gameId:int}/teams/{teamId:int}/cleanup")]
    public async Task<IActionResult> CleanupTeam(int gameId, int teamId, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        await adapter.DestroyTeamAsync(gameId, teamId, cancellationToken);
        return Ok(new RequestResponse("The TeamLab runtime was destroyed.", StatusCodes.Status200OK));
    }

    [HttpGet("games/{gameId:int}/scoreboard")]
    public async Task<IActionResult> GetScoreboard(int gameId, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        return error ?? Ok(await objectives.GetScoreboardAsync(gameId, cancellationToken));
    }

    [HttpGet("games/{gameId:int}/runtimes")]
    public async Task<IActionResult> GetRuntimes(int gameId, CancellationToken cancellationToken)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        return error ?? Ok(await adapter.ListRuntimesAsync(gameId, cancellationToken));
    }

    [HttpGet("games/{gameId:int}/submissions")]
    public async Task<IActionResult> GetSubmissions(
        int gameId,
        [FromQuery, Range(1, 100)] int count = 50,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var error = await ValidateGameAsync(gameId, cancellationToken);
        return error ?? Ok(await objectives.GetSubmissionLogsAsync(gameId, count, skip, cancellationToken));
    }

    private async Task<IActionResult?> ValidateGameAsync(int gameId, CancellationToken cancellationToken)
    {
        var game = await games.GetGameById(gameId, cancellationToken);
        if (game is null) return NotFound(new RequestResponse("Game not found."));
        if (game.GameType is not GameType.Penetration and not GameType.Mixed)
            return BadRequest(new RequestResponse("This game does not support penetration objectives."));
        return null;
    }
}

public sealed record BindPenetrationTopologyModel(Guid TopologyId);
