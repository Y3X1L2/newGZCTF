using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// AWD Administration APIs
/// </summary>
[RequireAdmin]
[ApiController]
[Route("api/admin/awd")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class AwdAdminController(
    AppDbContext context,
    IAwdRepository awdRepository,
    AwdInstanceService instanceService,
    AwdRoundService roundService,
    IGameRepository gameRepository,
    ILogger<AwdAdminController> logger) : ControllerBase
{
    /// <summary>
    /// Get all AWD services for a game
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Service list</response>
    /// <response code="404">Game not found</response>
    [HttpGet("games/{gameId:int}/services")]
    [ProducesResponseType(typeof(AwdServiceViewModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetServices(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        var services = await awdRepository.GetServicesByGame(gameId, token);
        return Ok(services.Select(s => new AwdServiceViewModel
        {
            Id = s.Id,
            Name = s.Name,
            ImageName = s.ImageName,
            ExposePort = s.ExposePort,
            AttackPoints = s.AttackPoints,
            SlaPoints = s.SlaPoints,
            RoundDurationMinutes = s.RoundDurationMinutes,
            TotalRounds = s.TotalRounds
        }).ToArray());
    }

    /// <summary>
    /// Create an AWD service for a game
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Service created</response>
    /// <response code="404">Game not found</response>
    /// <response code="400">Invalid request</response>
    [HttpPost("games/{gameId:int}/services")]
    [ProducesResponseType(typeof(AwdServiceViewModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateService(int gameId, [FromBody] AwdServiceCreateModel model, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        if (game.GameType is not GameType.AWD and not GameType.Mixed)
            return BadRequest(new RequestResponse("该比赛不是AWD或混合模式"));

        var service = new AwdService
        {
            GameId = gameId,
            Name = model.Name,
            ImageName = model.ImageName,
            ExposePort = model.ExposePort,
            CheckerScript = model.CheckerScript,
            CheckerEntrypoint = model.CheckerEntrypoint,
            AttackPoints = model.AttackPoints,
            SlaPoints = model.SlaPoints,
            MaxAttackPerRound = model.MaxAttackPerRound,
            RoundDurationMinutes = model.RoundDurationMinutes,
            TotalRounds = model.TotalRounds
        };

        context.AwdServices.Add(service);
        await context.SaveChangesAsync(token);

        return Ok(new AwdServiceViewModel
        {
            Id = service.Id,
            Name = service.Name,
            ImageName = service.ImageName,
            ExposePort = service.ExposePort,
            AttackPoints = service.AttackPoints,
            SlaPoints = service.SlaPoints,
            RoundDurationMinutes = service.RoundDurationMinutes,
            TotalRounds = service.TotalRounds
        });
    }

    /// <summary>
    /// Update an AWD service
    /// </summary>
    /// <param name="serviceId">Service ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Service updated</response>
    /// <response code="404">Service not found</response>
    [HttpPut("services/{serviceId:int}")]
    [ProducesResponseType(typeof(AwdServiceViewModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateService(int serviceId, [FromBody] AwdServiceUpdateModel model, CancellationToken token)
    {
        var service = await awdRepository.GetService(serviceId, token);
        if (service is null)
            return NotFound(new RequestResponse("服务未找到", StatusCodes.Status404NotFound));

        service.Name = model.Name;
        service.ImageName = model.ImageName;
        service.ExposePort = model.ExposePort;
        service.CheckerScript = model.CheckerScript;
        service.CheckerEntrypoint = model.CheckerEntrypoint;
        service.AttackPoints = model.AttackPoints;
        service.SlaPoints = model.SlaPoints;
        service.MaxAttackPerRound = model.MaxAttackPerRound;
        service.RoundDurationMinutes = model.RoundDurationMinutes;
        service.TotalRounds = model.TotalRounds;

        context.AwdServices.Update(service);
        await context.SaveChangesAsync(token);

        return Ok(new AwdServiceViewModel
        {
            Id = service.Id,
            Name = service.Name,
            ImageName = service.ImageName,
            ExposePort = service.ExposePort,
            AttackPoints = service.AttackPoints,
            SlaPoints = service.SlaPoints,
            RoundDurationMinutes = service.RoundDurationMinutes,
            TotalRounds = service.TotalRounds
        });
    }

    /// <summary>
    /// Delete an AWD service
    /// </summary>
    /// <param name="serviceId">Service ID</param>
    /// <param name="token"></param>
    /// <response code="204">Service deleted</response>
    /// <response code="404">Service not found</response>
    [HttpDelete("services/{serviceId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteService(int serviceId, CancellationToken token)
    {
        var service = await awdRepository.GetService(serviceId, token);
        if (service is null)
            return NotFound(new RequestResponse("服务未找到", StatusCodes.Status404NotFound));

        context.AwdServices.Remove(service);
        await context.SaveChangesAsync(token);

        return NoContent();
    }

    /// <summary>
    /// Start an AWD game
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Game started</response>
    /// <response code="404">Game not found</response>
    /// <response code="400">Invalid game type</response>
    [HttpPost("games/{gameId:int}/start")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartGame(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        if (game.GameType is not GameType.AWD and not GameType.Mixed)
            return BadRequest(new RequestResponse("该比赛不是AWD或混合模式"));

        roundService.StartGame(game);
        logger.LogInformation("AWD game {GameId} started by admin", gameId);

        return Ok();
    }

    /// <summary>
    /// Stop an AWD game
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <response code="200">Game stopped</response>
    [HttpPost("games/{gameId:int}/stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult StopGame(int gameId)
    {
        roundService.StopGame(gameId);
        logger.LogInformation("AWD game {GameId} stopped by admin", gameId);
        return Ok();
    }

    /// <summary>
    /// Reset an AWD instance
    /// </summary>
    /// <param name="instanceId">Instance ID</param>
    /// <param name="token"></param>
    /// <response code="200">Instance reset</response>
    /// <response code="404">Instance not found</response>
    [HttpPost("instances/{instanceId:int}/reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetInstance(int instanceId, CancellationToken token)
    {
        var instance = await awdRepository.GetInstance(instanceId, token);
        if (instance is null)
            return NotFound(new RequestResponse("实例未找到", StatusCodes.Status404NotFound));

        await instanceService.ResetInstance(instanceId, token: token);
        return Ok();
    }

    /// <summary>
    /// Get all AWD instances for a game
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Instance list</response>
    /// <response code="404">Game not found</response>
    [HttpGet("games/{gameId:int}/instances")]
    [ProducesResponseType(typeof(TeamServiceStatus[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInstances(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        var instances = await awdRepository.GetInstancesByGame(gameId, token);
        var result = instances.Select(i => new TeamServiceStatus
        {
            InstanceId = i.Id,
            TeamId = i.TeamId,
            TeamName = i.Team?.Name ?? string.Empty,
            IpAddress = i.Container?.IP,
            Port = i.Container?.Port,
            IsRunning = i.IsRunning && i.Container?.Status == ContainerStatus.Running
        }).ToArray();

        return Ok(result);
    }

    /// <summary>
    /// Get AWD game status
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Game status</response>
    /// <response code="404">Game not found</response>
    [HttpGet("games/{gameId:int}/status")]
    [ProducesResponseType(typeof(AwdGameStatusModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameStatus(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return NotFound(new RequestResponse("比赛未找到", StatusCodes.Status404NotFound));

        var services = await awdRepository.GetServicesByGame(gameId, token);
        var currentRound = roundService.GetCurrentRound(gameId);
        var round = currentRound.HasValue ? await awdRepository.GetRoundsByGame(gameId, token) : null;
        var currentRoundInfo = round?.FirstOrDefault(r => r.RoundNumber == currentRound);

        return Ok(new AwdGameStatusModel
        {
            GameId = gameId,
            CurrentRound = currentRound ?? 0,
            RoundStartTime = currentRoundInfo?.StartTime ?? DateTimeOffset.UtcNow,
            RoundDurationMinutes = services.FirstOrDefault()?.RoundDurationMinutes ?? 5,
            Status = currentRound.HasValue ? AwdRoundStatus.Running : AwdRoundStatus.Preparing
        });
    }
}
