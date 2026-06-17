using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Cache;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// AWDP administration APIs
/// </summary>
[RequireTeacher]
[ApiController]
[Route("api/admin/awdp")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class AwdpAdminController(
    AppDbContext context,
    IAwdpRepository awdpRepository,
    IGameRepository gameRepository,
    AwdpRoundService roundService,
    AwdpInstanceService instanceService,
    AwdpScoreService scoreService,
    CacheHelper cacheHelper) : ControllerBase
{
    [HttpGet("Games/{gameId:int}/Services")]
    [ProducesResponseType(typeof(AwdpServiceViewModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetServices([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await awdpRepository.GetServiceViewsByGame(gameId, token));
    }

    [HttpGet("Games/{gameId:int}/Scoreboard")]
    [ProducesResponseType(typeof(AwdpScoreboardItem[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScoreboard([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        var scoreboard = await gameRepository.GetScoreboard(validation.Game!, token);
        var ctfScores = scoreboard.Items.Values.ToDictionary(i => i.Id, i => i.CtfScore);

        return Ok(await scoreService.GetScoreboard(gameId, ctfScores, token));
    }

    [HttpPost("Games/{gameId:int}/Services")]
    [ProducesResponseType(typeof(AwdpServiceViewModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateService([FromRoute] int gameId,
        [FromBody] AwdpServiceCreateModel model, CancellationToken token)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        if (await ValidateScriptPrivilege(model, null, token) is { } privilegeError)
            return privilegeError;

        if (ValidateServiceModel(model) is { } error)
            return BadRequest(new RequestResponse(error));

        var service = new AwdpService { GameId = gameId };
        ApplyServiceModel(service, model);

        try
        {
            await awdpRepository.CreateService(service, token);
        }
        catch (DbUpdateException)
        {
            return BadRequest(new RequestResponse("AWDP service name must be unique in the game."));
        }

        await cacheHelper.FlushScoreboardCache(gameId, token);
        return Ok(ToViewModel(service));
    }

    [HttpPut("Services/{serviceId:int}")]
    [ProducesResponseType(typeof(AwdpServiceViewModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateService([FromRoute] int serviceId,
        [FromBody] AwdpServiceUpdateModel model, CancellationToken token)
    {
        var service = await awdpRepository.GetServiceForUpdate(serviceId, token);
        if (service is null)
            return NotFound(new RequestResponse("AWDP service not found.", StatusCodes.Status404NotFound));

        var validation = await ValidateAwdpGame(service.GameId, token);
        if (validation.Result is not null)
            return validation.Result;

        if (await ValidateScriptPrivilege(model, service, token) is { } privilegeError)
            return privilegeError;

        if (ValidateServiceModel(model) is { } error)
            return BadRequest(new RequestResponse(error));

        ApplyServiceModel(service, model);

        try
        {
            await awdpRepository.SaveAsync(token);
        }
        catch (DbUpdateException)
        {
            return BadRequest(new RequestResponse("AWDP service name must be unique in the game."));
        }

        await cacheHelper.FlushScoreboardCache(service.GameId, token);
        return Ok(ToViewModel(service));
    }

    [HttpDelete("Services/{serviceId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteService([FromRoute] int serviceId, CancellationToken token)
    {
        var service = await awdpRepository.GetServiceForUpdate(serviceId, token);
        if (service is null)
            return NotFound(new RequestResponse("AWDP service not found.", StatusCodes.Status404NotFound));

        var gameId = service.GameId;
        await instanceService.DestroyInstancesForService(service.Id, token);
        await awdpRepository.DeleteService(service, token);
        await cacheHelper.FlushScoreboardCache(gameId, token);

        return Ok();
    }

    [HttpPost("Games/{gameId:int}/Start")]
    [RequireAdmin]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartGame([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        var result = await roundService.StartGame(validation.Game!, token);
        return result.Success ? Ok(new RequestResponse(result.Message, StatusCodes.Status200OK)) :
            BadRequest(new RequestResponse(result.Message));
    }

    [HttpPost("Games/{gameId:int}/Stop")]
    [RequireAdmin]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StopGame([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        var result = await roundService.StopGame(gameId, true, token);
        return Ok(new RequestResponse(result.Message, StatusCodes.Status200OK));
    }

    [HttpGet("Games/{gameId:int}/Status")]
    [ProducesResponseType(typeof(AwdpGameStatusModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await roundService.GetStatus(gameId, token));
    }

    [HttpGet("Games/{gameId:int}/Instances")]
    [ProducesResponseType(typeof(AwdpServiceStatusModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInstances([FromRoute] int gameId, CancellationToken token)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        return Ok(await BuildServiceStatuses(gameId, null, token));
    }

    [HttpPost("Instances/{instanceId:int}/Reset")]
    [ProducesResponseType(typeof(AwdpInstanceActionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetInstance([FromRoute] int instanceId, CancellationToken token)
    {
        var result = await instanceService.ResetInstance(instanceId, null, token);
        return Ok(new AwdpInstanceActionModel
        {
            InstanceId = instanceId,
            Success = result.Success,
            Message = result.Message
        });
    }

    [HttpPost("Instances/{instanceId:int}/Recover")]
    [ProducesResponseType(typeof(AwdpInstanceActionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecoverInstance([FromRoute] int instanceId, CancellationToken token)
    {
        var result = await instanceService.RecoverInstance(instanceId, token);
        return Ok(new AwdpInstanceActionModel
        {
            InstanceId = instanceId,
            Success = result.Success,
            Message = result.Message
        });
    }

    [HttpGet("Games/{gameId:int}/Patches")]
    [ProducesResponseType(typeof(ArrayResponse<AwdpPatchSubmissionViewModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatches([FromRoute] int gameId,
        [FromQuery][Range(0, 100)] int count = 50, [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        var patches = await awdpRepository.GetPatchSubmissionsByGame(gameId, count, skip, token);
        var total = await context.AwdpPatchSubmissions.AsNoTracking()
            .CountAsync(p => p.Service.GameId == gameId, token);

        return Ok(patches.Select(ToPatchViewModel).ToResponse(total));
    }

    [HttpGet("Games/{gameId:int}/AttackLogs")]
    [ProducesResponseType(typeof(ArrayResponse<AwdpAttackLogItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAttackLogs([FromRoute] int gameId,
        [FromQuery][Range(0, 100)] int count = 50, [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var validation = await ValidateAwdpGame(gameId, token);
        if (validation.Result is not null)
            return validation.Result;

        var query = context.AwdpFlags.AsNoTracking()
            .Where(f => f.Round.GameId == gameId && f.IsSubmitted && f.SubmittedByTeamId != null);

        var total = await query.CountAsync(token);
        var logs = await query
            .OrderByDescending(f => f.FirstSubmittedAt)
            .Skip(Math.Max(0, skip))
            .Take(count <= 0 ? 50 : Math.Min(count, 100))
            .Select(f => new AwdpAttackLogItem
            {
                Time = f.FirstSubmittedAt ?? DateTimeOffset.MinValue,
                AttackerTeam = f.SubmittedByTeam == null ? string.Empty : f.SubmittedByTeam.Name,
                VictimTeam = f.Team.Name,
                ServiceName = f.Service.Name,
                Points = f.Service.AttackPoints
            })
            .ToArrayAsync(token);

        return Ok(logs.ToResponse(total));
    }

    async Task<(Game? Game, IActionResult? Result)> ValidateAwdpGame(int gameId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(gameId, token);
        if (game is null)
            return (null, NotFound(new RequestResponse("Game not found.", StatusCodes.Status404NotFound)));

        if (game.GameType is not GameType.AWDP and not GameType.Mixed)
            return (game, BadRequest(new RequestResponse("The game is not an AWDP or mixed game.")));

        return (game, null);
    }

    async Task<AwdpServiceStatusModel[]> BuildServiceStatuses(int gameId, int? teamId, CancellationToken token)
    {
        var services = await awdpRepository.GetServicesByGame(gameId, token);
        var instances = await awdpRepository.GetInstancesByGame(gameId, token);
        var round = await awdpRepository.GetCurrentRound(gameId, token);
        var checkerTasks = round is null
            ? Array.Empty<AwdpCheckerTask>()
            : await awdpRepository.GetCheckerTasksByRound(round.Id, token);
        var patchSubmissions = round is null
            ? Array.Empty<AwdpPatchSubmission>()
            : await awdpRepository.GetPatchSubmissionsByRound(round.Id, token);
        var resets = await awdpRepository.GetResetRecordsByGame(gameId, token);
        var recoveries = await awdpRepository.GetRecoveryRecordsByGame(gameId, token);

        return services.Select(service => new AwdpServiceStatusModel
        {
            ServiceId = service.Id,
            ServiceName = service.Name,
            TeamStatuses = instances.Where(i => i.ServiceId == service.Id && (!teamId.HasValue || i.TeamId == teamId))
                .Select(i => new AwdpTeamServiceStatus
                {
                    InstanceId = i.Id,
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    TeamId = i.TeamId,
                    TeamName = i.Team.Name,
                    IpAddress = i.Container?.PublicIP ?? i.Container?.IP,
                    Port = i.Container?.PublicPort ?? i.Container?.Port,
                    LastCheckerStatus = AwdpPatchStateResolver.ResolveLatestCheckerStatus(service.Id, i.TeamId,
                        checkerTasks, patchSubmissions, resets, recoveries, round?.StartTime, round?.EndTime),
                    IsRunning = i.IsRunning && i.Container?.Status == ContainerStatus.Running,
                    RemainingResetCount = Math.Max(0,
                        service.MaxResetCount - resets.Count(r =>
                            r.ServiceId == service.Id && r.TeamId == i.TeamId &&
                            r.ResetType == AwdpResetType.Player)),
                    RemainingRecoveryCount = Math.Max(0,
                        service.MaxRecoveryCount -
                        recoveries.Count(r => r.ServiceId == service.Id && r.TeamId == i.TeamId)),
                    CanManage = true
                }).ToList()
        }).ToArray();
    }

    static string? ValidateServiceModel(AwdpServiceCreateModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return "Service name is required.";

        if (string.IsNullOrWhiteSpace(model.ImageName))
            return "Container image is required.";

        if (model.ExposePort is <= 0 or > 65535)
            return "Expose port must be between 1 and 65535.";

        if (model.OriginalScore < 0 || model.AttackPoints < 0 || model.SlaPoints < 0 ||
            model.PatchPoints < 0 || model.ServiceAbnormalPenalty < 0)
            return "Score and penalty values must be greater than or equal to 0.";

        if (model.MaxAttackPerRound <= 0 || model.AttackPhaseMinutes <= 0 ||
            model.PatchPhaseMinutes <= 0 || model.TotalRounds <= 0)
            return "Round limits and phase durations must be greater than 0.";

        if (model.MaxResetCount < 0 || model.MaxRecoveryCount < 0)
            return "Reset and recovery limits must be greater than or equal to 0.";

        if ((model.CheckerScript?.Length ?? 0) > Limits.MaxScriptLength ||
            (model.ExpScript?.Length ?? 0) > Limits.MaxScriptLength)
            return "Checker and Exp scripts are too long.";

        if ((model.CheckerEntrypoint?.Length ?? 0) > Limits.MaxEntrypointLength ||
            (model.ExpEntrypoint?.Length ?? 0) > Limits.MaxEntrypointLength)
            return "Checker and Exp entrypoints are too long.";

        if (!IsAllowedPythonEntrypoint(model.CheckerEntrypoint, "checker.py") ||
            !IsAllowedPythonEntrypoint(model.ExpEntrypoint, "exp.py"))
            return "AWDP script entrypoints must be in the form: python3 checker.py or python3 exp.py.";

        return null;
    }

    async Task<IActionResult?> ValidateScriptPrivilege(AwdpServiceCreateModel model, AwdpService? existing,
        CancellationToken token)
    {
        if (!ChangesScriptExecution(model, existing))
            return null;

        if (await ContextHelper.HasAdmin(HttpContext))
            return null;

        return Forbid();
    }

    static bool ChangesScriptExecution(AwdpServiceCreateModel model, AwdpService? existing)
    {
        if (existing is null)
            return !string.IsNullOrWhiteSpace(model.CheckerScript) ||
                   !string.IsNullOrWhiteSpace(model.ExpScript) ||
                   !string.IsNullOrWhiteSpace(model.CheckerEntrypoint) && model.CheckerEntrypoint.Trim() != "python3 checker.py" ||
                   !string.IsNullOrWhiteSpace(model.ExpEntrypoint) && model.ExpEntrypoint.Trim() != "python3 exp.py";

        return NormalizeScript(model.CheckerScript) != NormalizeScript(existing.CheckerScript) ||
               NormalizeScript(model.ExpScript) != NormalizeScript(existing.ExpScript) ||
               NormalizeEntrypoint(model.CheckerEntrypoint, "python3 checker.py") !=
               NormalizeEntrypoint(existing.CheckerEntrypoint, "python3 checker.py") ||
               NormalizeEntrypoint(model.ExpEntrypoint, "python3 exp.py") !=
               NormalizeEntrypoint(existing.ExpEntrypoint, "python3 exp.py");
    }

    static string? NormalizeScript(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    static string NormalizeEntrypoint(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    static bool IsAllowedPythonEntrypoint(string? value, string fileName)
    {
        var entrypoint = NormalizeEntrypoint(value, $"python3 {fileName}");
        return entrypoint == $"python3 {fileName}" || entrypoint == $"python {fileName}";
    }

    static void ApplyServiceModel(AwdpService service, AwdpServiceCreateModel model)
    {
        service.Name = model.Name.Trim();
        service.ImageName = model.ImageName.Trim();
        service.ExposePort = model.ExposePort;
        service.CheckerScript = string.IsNullOrWhiteSpace(model.CheckerScript) ? null : model.CheckerScript;
        service.CheckerEntrypoint = string.IsNullOrWhiteSpace(model.CheckerEntrypoint)
            ? "python3 checker.py"
            : model.CheckerEntrypoint.Trim();
        service.ExpScript = string.IsNullOrWhiteSpace(model.ExpScript) ? null : model.ExpScript;
        service.ExpEntrypoint = string.IsNullOrWhiteSpace(model.ExpEntrypoint)
            ? "python3 exp.py"
            : model.ExpEntrypoint.Trim();
        service.OriginalScore = model.OriginalScore;
        service.AttackPoints = model.AttackPoints;
        service.SlaPoints = model.SlaPoints;
        service.PatchPoints = model.PatchPoints;
        service.ServiceAbnormalPenalty = model.ServiceAbnormalPenalty;
        service.MaxAttackPerRound = model.MaxAttackPerRound;
        service.AttackPhaseMinutes = model.AttackPhaseMinutes;
        service.PatchPhaseMinutes = model.PatchPhaseMinutes;
        service.TotalRounds = model.TotalRounds;
        service.MaxResetCount = model.MaxResetCount;
        service.MaxRecoveryCount = model.MaxRecoveryCount;
    }

    static AwdpServiceViewModel ToViewModel(AwdpService service) => new()
    {
        Id = service.Id,
        Name = service.Name,
        ImageName = service.ImageName,
        ExposePort = service.ExposePort,
        CheckerScript = service.CheckerScript,
        CheckerEntrypoint = service.CheckerEntrypoint,
        ExpScript = service.ExpScript,
        ExpEntrypoint = service.ExpEntrypoint,
        OriginalScore = service.OriginalScore,
        AttackPoints = service.AttackPoints,
        SlaPoints = service.SlaPoints,
        PatchPoints = service.PatchPoints,
        ServiceAbnormalPenalty = service.ServiceAbnormalPenalty,
        MaxAttackPerRound = service.MaxAttackPerRound,
        AttackPhaseMinutes = service.AttackPhaseMinutes,
        PatchPhaseMinutes = service.PatchPhaseMinutes,
        TotalRounds = service.TotalRounds,
        MaxResetCount = service.MaxResetCount,
        MaxRecoveryCount = service.MaxRecoveryCount
    };

    static AwdpPatchSubmissionViewModel ToPatchViewModel(AwdpPatchSubmission patch) => new()
    {
        Id = patch.Id,
        RoundId = patch.RoundId,
        RoundNumber = patch.Round.RoundNumber,
        ServiceId = patch.ServiceId,
        ServiceName = patch.Service.Name,
        TeamId = patch.TeamId,
        TeamName = patch.Team.Name,
        PatchFileHash = patch.PatchFileHash,
        SubmittedAt = patch.SubmittedAt,
        CheckerResult = patch.CheckerResult,
        ExpResult = patch.ExpResult,
        FinalStatus = patch.FinalStatus,
        Message = patch.Message
    };
}
