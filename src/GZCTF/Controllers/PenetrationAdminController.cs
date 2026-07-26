using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Modules.Penetration.Application;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        var (_, _, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        var binding = await adapter.GetBindingAsync(gameId, cancellationToken);
        return binding is null ? NotFound(new RequestResponse("The game has no TeamLab topology binding.")) : Ok(binding);
    }

    [HttpPut("games/{gameId:int}/binding")]
    public async Task<IActionResult> Bind(int gameId, BindPenetrationTopologyModel model, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return await ExecuteTeamLabAsync(() => adapter.BindAsync(
            gameId, model.TopologyId, actor!.Id, actor.Role >= Role.Admin, cancellationToken));
    }

    [HttpPut("games/{gameId:int}/objectives")]
    [ProducesResponseType(typeof(PenetrationGameLabBindingModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceObjectives(
        int gameId,
        ReplacePenetrationObjectivesModel model,
        CancellationToken cancellationToken)
    {
        var (_, _, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        try
        {
            await objectives.ReplaceAsync(gameId, model, cancellationToken);
            return Ok(await adapter.GetBindingAsync(gameId, cancellationToken));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new RequestResponse(
                "The scoring objective configuration changed. Reload it and try again.",
                StatusCodes.Status409Conflict));
        }
        catch (TeamLabApiContractException exception)
        {
            return StatusCode(exception.StatusCode, new RequestResponse(exception.Message, exception.StatusCode));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new RequestResponse(exception.Message, StatusCodes.Status409Conflict));
        }
    }

    [HttpPost("games/{gameId:int}/releases/{releaseId:guid}/activate")]
    public async Task<IActionResult> ActivateRelease(int gameId, Guid releaseId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return await ExecuteTeamLabAsync(() => adapter.ActivateReleaseAsync(
            gameId, releaseId, actor!.Id, actor.Role >= Role.Admin, cancellationToken));
    }

    [HttpPost("games/{gameId:int}/deploy")]
    public async Task<IActionResult> Deploy(int gameId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return await ExecuteTeamLabAsync(() => adapter.PrepareAsync(
            gameId, actor!.Id, actor.Role >= Role.Admin, cancellationToken), StatusCodes.Status202Accepted);
    }

    [HttpPost("games/{gameId:int}/stop")]
    public async Task<IActionResult> Stop(int gameId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return await ExecuteTeamLabAsync(() => adapter.DrainAsync(
            gameId, actor!.Id, actor.Role >= Role.Admin, cancellationToken), StatusCodes.Status202Accepted);
    }

    [HttpGet("games/{gameId:int}/teamlab")]
    public async Task<IActionResult> GetTeamLab(int gameId, CancellationToken cancellationToken)
    {
        var (_, _, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        return error ?? Ok(await adapter.GetGameTeamLabAsync(gameId, cancellationToken));
    }

    [HttpGet("games/{gameId:int}/teamlab/releases")]
    public async Task<IActionResult> ListTeamLabReleases(int gameId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return Ok(await adapter.ListAvailableReleasesAsync(
            actor!.Id, actor.Role >= Role.Admin, cancellationToken));
    }

    [HttpPost("games/{gameId:int}/teamlab/prepare")]
    public async Task<IActionResult> PrepareTeamLab(int gameId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return await ExecuteTeamLabAsync(() => adapter.PrepareAsync(
            gameId, actor!.Id, actor.Role >= Role.Admin, cancellationToken), StatusCodes.Status202Accepted);
    }

    [HttpPost("games/{gameId:int}/teamlab/access/open")]
    public async Task<IActionResult> OpenTeamLabAccess(int gameId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return await ExecuteTeamLabAsync(() => adapter.SetAccessAsync(
            gameId, actor!.Id, actor.Role >= Role.Admin, true, cancellationToken));
    }

    [HttpPost("games/{gameId:int}/teamlab/access/close")]
    public async Task<IActionResult> CloseTeamLabAccess(int gameId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return await ExecuteTeamLabAsync(() => adapter.SetAccessAsync(
            gameId, actor!.Id, actor.Role >= Role.Admin, false, cancellationToken));
    }

    [HttpPost("games/{gameId:int}/teamlab/drain")]
    public async Task<IActionResult> DrainTeamLab(int gameId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        return await ExecuteTeamLabAsync(() => adapter.DrainAsync(
            gameId, actor!.Id, actor.Role >= Role.Admin, cancellationToken), StatusCodes.Status202Accepted);
    }

    [HttpGet("games/{gameId:int}/teamlab/targets")]
    public async Task<IActionResult> ListTeamLabTargets(
        int gameId,
        [FromQuery] string? after,
        [FromQuery, Range(1, 100)] int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var (_, _, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        return error ?? Ok(await adapter.ListRolloutTargetsAsync(gameId, after, limit, cancellationToken));
    }

    [HttpPost("games/{gameId:int}/teams/{teamId:int}/rebuild")]
    public async Task<IActionResult> RebuildTeam(int gameId, int teamId, CancellationToken cancellationToken)
    {
        var (_, actor, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        var result = await adapter.ResetTeamAsync(gameId, teamId, actor!.Id, true, cancellationToken);
        return Accepted(new { runtimeId = result.RuntimePublicId, result.Reused });
    }

    [HttpPost("games/{gameId:int}/teams/{teamId:int}/cleanup")]
    public async Task<IActionResult> CleanupTeam(int gameId, int teamId, CancellationToken cancellationToken)
    {
        var (_, _, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        if (error is not null) return error;
        await adapter.DestroyTeamAsync(gameId, teamId, cancellationToken);
        return Ok(new RequestResponse("The TeamLab runtime was destroyed.", StatusCodes.Status200OK));
    }

    [HttpGet("games/{gameId:int}/scoreboard")]
    public async Task<IActionResult> GetScoreboard(int gameId, CancellationToken cancellationToken)
    {
        var (_, _, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        return error ?? Ok(await objectives.GetScoreboardAsync(gameId, cancellationToken));
    }

    [HttpGet("games/{gameId:int}/runtimes")]
    public async Task<IActionResult> GetRuntimes(int gameId, CancellationToken cancellationToken)
    {
        var (_, _, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        return error ?? Ok(await adapter.ListRuntimesAsync(gameId, cancellationToken));
    }

    [HttpGet("games/{gameId:int}/submissions")]
    public async Task<IActionResult> GetSubmissions(
        int gameId,
        [FromQuery, Range(1, 100)] int count = 50,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var (_, _, error) = await RequireManageableGameAsync(gameId, cancellationToken);
        return error ?? Ok(await objectives.GetSubmissionLogsAsync(gameId, count, skip, cancellationToken));
    }

    private async Task<(Game? Game, UserInfo? Actor, IActionResult? Error)> RequireManageableGameAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        var game = await games.GetGameById(gameId, cancellationToken);
        if (game is null) return (null, null, NotFound(new RequestResponse("Game not found.")));
        var actor = await users.GetUserAsync(User);
        if (actor is null)
            return (null, null, Unauthorized(new RequestResponse("Login required.")));
        if (!ResourceOwnershipPolicy.CanManage(game.OwnerId, actor.Id, actor.Role))
            return (null, null, StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse("You do not manage this game.", StatusCodes.Status403Forbidden)));
        if (game.GameType is not GameType.Penetration and not GameType.Mixed)
            return (null, null, BadRequest(new RequestResponse("This game does not support penetration objectives.")));
        return (game, actor, null);
    }

    private async Task<IActionResult> ExecuteTeamLabAsync<T>(
        Func<Task<T>> action,
        int successStatus = StatusCodes.Status200OK)
    {
        try
        {
            return StatusCode(successStatus, await action());
        }
        catch (TeamLabApiContractException exception)
        {
            return StatusCode(exception.StatusCode, new RequestResponse(exception.Message, exception.StatusCode));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new RequestResponse(exception.Message, StatusCodes.Status409Conflict));
        }
    }
}

public sealed record BindPenetrationTopologyModel(Guid TopologyId);
