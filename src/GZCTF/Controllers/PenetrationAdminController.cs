using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GZCTF.Controllers;

[RequireAdmin]
[ApiController]
[Route("api/admin/pentest")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class PenetrationAdminController(
    IGameRepository gameRepository,
    PenetrationService penetrationService) : ControllerBase
{
    [HttpGet("games/{gameId:int}")]
    [ProducesResponseType(typeof(PenetrationConfigModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfig([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await penetrationService.GetOrCreateConfig(gameId, token));
    }

    [HttpPut("games/{gameId:int}")]
    [ProducesResponseType(typeof(PenetrationConfigModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveConfig([FromRoute] int gameId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PenetrationConfigModel? model,
        CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        if (model is null)
            return BadRequest(new RequestResponse("渗透编排配置不能为空。"));

        return Ok(await penetrationService.SaveConfig(gameId, model, token));
    }

    [HttpPost("games/{gameId:int}/validate")]
    [ProducesResponseType(typeof(PenetrationValidationModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate([FromRoute] int gameId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PenetrationConfigModel? model,
        CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        if (model is not null)
            await penetrationService.SaveConfig(gameId, model, token);

        return Ok(await penetrationService.Validate(gameId, token));
    }

    [HttpPost("games/{gameId:int}/plan")]
    [ProducesResponseType(typeof(PenetrationPlanModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlan([FromRoute] int gameId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PenetrationConfigModel? model,
        CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        if (model is not null)
            await penetrationService.SaveConfig(gameId, model, token);

        return Ok(await penetrationService.GetPlan(gameId, token));
    }

    [HttpPost("games/{gameId:int}/publish")]
    [ProducesResponseType(typeof(PenetrationConfigModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Publish([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        try
        {
            return Ok(await penetrationService.Publish(gameId, token));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new RequestResponse(ex.Message));
        }
    }

    [HttpPost("games/{gameId:int}/deploy")]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Deploy([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        var result = await penetrationService.DeployGame(gameId, token);
        return result.Success ? Ok(new RequestResponse(result.Message, StatusCodes.Status200OK))
            : BadRequest(new RequestResponse(result.Message));
    }

    [HttpPost("games/{gameId:int}/stop")]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        var result = await penetrationService.StopGame(gameId, token);
        return Ok(new RequestResponse(result.Message, StatusCodes.Status200OK));
    }

    [HttpPost("games/{gameId:int}/teams/{teamId:int}/rebuild")]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RebuildTeam([FromRoute] int gameId, [FromRoute] int teamId,
        CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        var result = await penetrationService.RebuildTeam(gameId, teamId, true, null, token);
        return result.Success ? Ok(new RequestResponse(result.Message, StatusCodes.Status200OK))
            : BadRequest(new RequestResponse(result.Message));
    }

    [HttpGet("games/{gameId:int}/teams/{teamId:int}/access")]
    [ProducesResponseType(typeof(PenetrationAdminAccessModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamAccess([FromRoute] int gameId, [FromRoute] int teamId,
        CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await penetrationService.GetAdminAccess(gameId, teamId, token));
    }

    [HttpGet("games/{gameId:int}/access")]
    [ProducesResponseType(typeof(PenetrationAdminAccessModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccess([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await penetrationService.GetAdminAccess(gameId, 0, token));
    }

    [HttpPost("runtime-nodes/{runtimeNodeId:int}/restart")]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestartRuntimeNode([FromRoute] int runtimeNodeId, CancellationToken token)
    {
        var result = await penetrationService.RestartRuntimeNode(runtimeNodeId, token);
        return result.Success ? Ok(new RequestResponse(result.Message, StatusCodes.Status200OK))
            : BadRequest(new RequestResponse(result.Message));
    }

    [HttpGet("games/{gameId:int}/scoreboard")]
    [ProducesResponseType(typeof(PenetrationScoreboardItemModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScoreboard([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await penetrationService.GetScoreboard(gameId, token));
    }

    [HttpGet("games/{gameId:int}/environments")]
    [ProducesResponseType(typeof(PenetrationTeamEnvironmentModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamEnvironments([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await penetrationService.GetTeamEnvironments(gameId, token));
    }

    [HttpGet("games/{gameId:int}/submissions")]
    [ProducesResponseType(typeof(ArrayResponse<PenetrationSubmissionLogModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubmissions([FromRoute] int gameId,
        [FromQuery][Range(0, 100)] int count = 50, [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var validation = await ValidatePentestGame(gameId, allowMixed: true, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await penetrationService.GetSubmissionLogs(gameId, count, skip, token));
    }

    private async Task<(Game? Game, IActionResult? Result)> ValidatePentestGame(int gameId, bool allowMixed,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return (null, NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound)));

        if (game.GameType != GameType.Penetration && !(allowMixed && game.GameType == GameType.Mixed))
            return (null, BadRequest(new RequestResponse("This game does not support penetration topology.")));

        return (game, null);
    }
}
