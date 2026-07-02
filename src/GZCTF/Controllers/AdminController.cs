using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Admin;
using GZCTF.Models.Request.Info;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Config;
using GZCTF.Storage.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

/// <summary>
/// Administration APIs
/// </summary>
[RequireTeacher]
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class AdminController(
    AppDbContext context,
    UserManager<UserInfo> userManager,
    ILogger<AdminController> logger,
    IBlobStorage storage,
    CacheHelper cacheHelper,
    IBlobRepository blobService,
    ILogRepository logRepository,
    IConfigService configService,
    IGameRepository gameRepository,
    ITeamRepository teamRepository,
    IContainerRepository containerRepository,
    IServiceProvider serviceProvider,
    IParticipationRepository participationRepository,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    /// <summary>
    /// Get configuration
    /// </summary>
    /// <remarks>
    /// Use this API to get global settings, requires Admin permission
    /// </remarks>
    /// <response code="200">Global configuration</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Config")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ConfigEditModel), StatusCodes.Status200OK)]
    public IActionResult GetConfigs()
    {
        // always reload, ensure latest
        configService.ReloadConfig();

        ConfigEditModel config = new()
        {
            AccountPolicy = serviceProvider.GetRequiredService<IOptionsSnapshot<AccountPolicy>>().Value,
            GlobalConfig = serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value,
            ContainerPolicy = serviceProvider.GetRequiredService<IOptionsSnapshot<ContainerPolicy>>().Value
        };

        return Ok(config);
    }

    /// <summary>
    /// Change configuration
    /// </summary>
    /// <remarks>
    /// Use this API to change global settings, requires Admin permission
    /// </remarks>
    /// <response code="200">Update successful</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPut("Config")]
    [RequireAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfigs([FromBody] ConfigEditModel model, CancellationToken token)
    {
        // handle api encryption config
        var global = serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value;
        if (!global.ApiEncryption && model.GlobalConfig?.ApiEncryption is true)
            await configService.UpdateApiEncryptionKey(token);

        // save all config properties
        foreach (var prop in typeof(ConfigEditModel).GetProperties())
        {
            var value = prop.GetValue(model);

            if (value is null)
                continue;

            await configService.SaveConfig(prop.PropertyType, value, token);
        }

        return Ok();
    }

    /// <summary>
    /// Change platform Logo
    /// </summary>
    /// <remarks>
    /// Use this API to change the platform Logo, requires Admin permission
    /// </remarks>
    /// <response code="200">Update successful</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPost("Config/Logo")]
    [RequireAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLogo(IFormFile file, CancellationToken token)
    {
        switch (file.Length)
        {
            case 0:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 3 * 1024 * 1024:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        if (!await DeleteCurrentLogo(token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_LogoUpdateFailed)]));

        var logo = await blobService.CreateOrUpdateImage(file, "logo", 640, token);
        if (logo is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_LogoUpdateFailed)]));

        var favicon = await blobService.CreateOrUpdateImage(file, "favicon", 256, token);
        if (favicon is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_LogoUpdateFailed)]));

        HashSet<Config> configSet =
        [
            new($"{nameof(GlobalConfig)}:{nameof(GlobalConfig.LogoHash)}", logo.Hash, [CacheKey.ClientConfig]),
            new($"{nameof(GlobalConfig)}:{nameof(GlobalConfig.FaviconHash)}", favicon.Hash, [CacheKey.Favicon])
        ];

        await configService.SaveConfigSet(configSet, token);

        return Ok();
    }

    /// <summary>
    /// Reset platform Logo
    /// </summary>
    /// <remarks>
    /// Use this API to reset the platform Logo, requires Admin permission
    /// </remarks>
    /// <response code="200">Updated successfully</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpDelete("Config/Logo")]
    [RequireAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetLogo(CancellationToken token)
    {
        if (!await DeleteCurrentLogo(token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_LogoUpdateFailed)]));

        HashSet<Config> configSet =
        [
            new($"{nameof(GlobalConfig)}:{nameof(GlobalConfig.LogoHash)}", string.Empty, [CacheKey.ClientConfig]),
            new($"{nameof(GlobalConfig)}:{nameof(GlobalConfig.FaviconHash)}", string.Empty, [CacheKey.Favicon])
        ];

        await configService.SaveConfigSet(configSet, token);

        return Ok();
    }

    private async Task<bool> DeleteCurrentLogo(CancellationToken token)
    {
        var globalConfig = serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value;

        return await DeleteByHash(globalConfig.LogoHash, token) &&
               await DeleteByHash(globalConfig.FaviconHash, token);
    }

    private async Task<bool> DeleteByHash(string? hash, CancellationToken token)
    {
        if (hash is not null && Codec.FileHashRegex().IsMatch(hash))
            return await blobService.DeleteBlobByHash(hash, token) switch
            {
                TaskStatus.Success or TaskStatus.NotFound => true,
                _ => false
            };

        return true;
    }

    /// <summary>
    /// Get all users
    /// </summary>
    /// <remarks>
    /// Use this API to get all users, requires Admin permission
    /// </remarks>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Users")]
    [ProducesResponseType(typeof(ArrayResponse<UserInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Users([FromQuery][Range(0, 500)] int count = 100, [FromQuery] int skip = 0,
        [FromQuery] Role? role = null, [FromQuery] int? groupId = null, [FromQuery] string? keyword = null,
        CancellationToken token = default)
    {
        var actor = await userManager.GetUserAsync(User);
        if (actor is null)
            return Unauthorized();

        var query = FilterVisibleUsers(actor, userManager.Users, groupId);

        if (role.HasValue)
            query = query.Where(u => u.Role == role.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var lowered = keyword.Trim().ToLower();
            query = query.Where(item =>
                item.UserName!.ToLower().Contains(lowered) ||
                item.StdNumber.ToLower().Contains(lowered) ||
                item.Email!.ToLower().Contains(lowered) ||
                item.PhoneNumber!.ToLower().Contains(lowered) ||
                item.Id.ToString().ToLower().Contains(lowered) ||
                item.RealName.ToLower().Contains(lowered));
        }

        var total = await query.CountAsync(token);
        var users = await query.OrderBy(e => e.Id).Skip(skip).Take(count).ToArrayAsync(token);
        var groups = await GetUserGroups(users.Select(u => u.Id).ToArray(), token);

        return Ok(users.Select(u => FillUserGroups(UserInfoModel.FromUserInfo(u), groups)).ToArray().ToResponse(total));
    }

    /// <summary>
    /// Add users in batch
    /// </summary>
    /// <remarks>
    /// Use this API to add users in batch, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully added</response>
    /// <response code="400">User validation failed</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPost("Users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddUsers([FromBody] UserCreateModel[] model, CancellationToken token = default)
    {
        var currentUser = await userManager.GetUserAsync(User);
        var trans = await teamRepository.BeginTransactionAsync(token);

        try
        {
            var users = new List<(UserInfo, string?)>(model.Length);
            foreach (var user in model)
            {
                var userInfo = user.ToUserInfo();
                var requestedRole = user.AssignedRole ?? Role.Student;
                if (!RolePolicy.CanAssignRole(currentUser!.Role, requestedRole))
                    return Forbid();

                userInfo.Role = requestedRole;
                var studentGroupIds = await ResolveStudentGroupsForCreatedUser(currentUser!, requestedRole, user.StudentGroupIds, token);
                var result = await userManager.CreateAsync(userInfo, user.Password);

                if (!result.Succeeded)
                {
                    userInfo = result.Errors.FirstOrDefault()?.Code switch
                    {
                        "DuplicateEmail" => await userManager.FindByEmailAsync(user.Email),
                        "DuplicateUserName" => await userManager.FindByNameAsync(user.UserName),
                        _ => null
                    };

                    if (userInfo is null)
                    {
                        await trans.RollbackAsync(token);
                        return HandleIdentityError(result.Errors);
                    }

                    if (!RolePolicy.CanManageRole(currentUser!.Role, userInfo.Role))
                    {
                        await trans.RollbackAsync(token);
                        return Forbid();
                    }

                    userInfo.UpdateUserInfo(user);
                    var code = await userManager.GeneratePasswordResetTokenAsync(userInfo);
                    await userManager.ResetPasswordAsync(userInfo, code, user.Password);
                }

                if (!await CanSyncStudentGroups(currentUser!, userInfo, studentGroupIds, token))
                {
                    await trans.RollbackAsync(token);
                    return Forbid();
                }
                await SyncStudentGroups(currentUser!, userInfo, studentGroupIds, token);

                users.Add((userInfo, user.TeamName));
            }

            var teams = new List<Team>();
            foreach (var (user, teamName) in users)
            {
                if (teamName is null)
                    continue;

                var team = teams.Find(team => team.Name == teamName);
                if (team is null)
                {
                    team = await teamRepository.CreateTeam(new() { Name = teamName }, user, token);
                    teams.Add(team);
                }
                else
                {
                    team.Members.Add(user);
                }
            }

            await teamRepository.SaveAsync(token);
            await trans.CommitAsync(token);

            logger.Log(StaticLocalizer[nameof(Resources.Program.Admin_UserBatchAdded), users.Count],
                currentUser, TaskStatus.Success);

            return Ok();
        }
        catch
        {
            await trans.RollbackAsync(token);
            throw;
        }
    }

    /// <summary>
    /// Search users
    /// </summary>
    /// <remarks>
    /// Use this API to search users, requires Admin permission
    /// </remarks>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPost("Users/Search")]
    [ProducesResponseType(typeof(ArrayResponse<UserInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchUsers([FromQuery] string hint, CancellationToken token = default)
    {
        var actor = await userManager.GetUserAsync(User);
        if (actor is null)
            return Unauthorized();

        var loweredHint = hint.ToLower();
        var data = await FilterVisibleUsers(actor, userManager.Users).Where(item =>
            item.UserName!.ToLower().Contains(loweredHint) ||
            item.StdNumber.ToLower().Contains(loweredHint) ||
            item.Email!.ToLower().Contains(loweredHint) ||
            item.PhoneNumber!.ToLower().Contains(loweredHint) ||
            item.Id.ToString().ToLower().Contains(loweredHint) ||
            item.RealName.ToLower().Contains(loweredHint)
        ).OrderBy(e => e.Id).Take(30).ToArrayAsync(token);

        var groups = await GetUserGroups(data.Select(u => u.Id).ToArray(), token);

        return Ok(data.Select(u => FillUserGroups(UserInfoModel.FromUserInfo(u), groups)).ToResponse());
    }

    /// <summary>
    /// Get all team information
    /// </summary>
    /// <remarks>
    /// Use this API to get all teams, requires Admin permission
    /// </remarks>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Teams")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ArrayResponse<TeamInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Teams([FromQuery][Range(0, 500)] int count = 100, [FromQuery] int skip = 0,
        CancellationToken token = default) =>
        Ok((await teamRepository.GetTeams(count, skip, token)).Select(team => TeamInfoModel.FromTeam(team))
            .ToResponse(await teamRepository.CountAsync(token)));

    /// <summary>
    /// Search teams
    /// </summary>
    /// <remarks>
    /// Use this API to search teams, requires Admin permission
    /// </remarks>
    /// <response code="200">User list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpPost("Teams/Search")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ArrayResponse<TeamInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchTeams([FromQuery] string hint, CancellationToken token = default) =>
        Ok((await teamRepository.SearchTeams(hint, token))
            .Select(team => TeamInfoModel.FromTeam(team))
            .ToResponse());

    /// <summary>
    /// Modify team information
    /// </summary>
    /// <remarks>
    /// Use this API to modify team information, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully updated</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Team not found</response>
    [HttpPut("Teams/{id:int}")]
    [RequireAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTeam([FromRoute] int id, [FromBody] AdminTeamModel model,
        CancellationToken token = default)
    {
        var team = await teamRepository.GetTeamById(id, token);

        if (team is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Team_NotFound)]));

        team.UpdateInfo(model);
        await teamRepository.SaveAsync(token);

        return Ok();
    }

    /// <summary>
    /// Modify user information
    /// </summary>
    /// <remarks>
    /// Use this API to modify user information, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully updated</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">User not found</response>
    [HttpPut("Users/{userid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserInfo(string userid, [FromBody] AdminUserInfoModel model)
    {
        var user = await userManager.FindByIdAsync(userid);

        if (user is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_UserNotFound)],
                StatusCodes.Status404NotFound));

        var actor = await userManager.GetUserAsync(User);
        if (actor is null)
            return Unauthorized();

        if (!RolePolicy.CanManageRole(actor.Role, user.Role))
            return Forbid();

        if (model.Role.HasValue && !RolePolicy.CanAssignRole(actor.Role, model.Role.Value))
            return Forbid();

        if (user.Role == Role.SuperAdmin && model.Role.HasValue && model.Role.Value != Role.SuperAdmin &&
            await userManager.Users.CountAsync(u => u.Role == Role.SuperAdmin) <= 1)
            return BadRequest(new RequestResponse("不能降级最后一个超级管理员。"));

        if (model.StudentGroupIds is not null && !await CanSyncStudentGroups(actor, user, model.StudentGroupIds, HttpContext.RequestAborted))
            return Forbid();

        if (model.UserName is not null && model.UserName != user.UserName)
        {
            var result = await userManager.SetUserNameAsync(user, model.UserName);

            if (!result.Succeeded)
                return HandleIdentityError(result.Errors);
        }

        if (model.Email is not null && model.Email != user.Email)
        {
            var result = await userManager.SetEmailAsync(user, model.Email);

            if (!result.Succeeded)
                return HandleIdentityError(result.Errors);
        }

        user.UpdateUserInfo(model);
        await userManager.UpdateAsync(user);
        if (model.StudentGroupIds is not null)
        {
            await SyncStudentGroups(actor, user, model.StudentGroupIds, HttpContext.RequestAborted);
            await context.SaveChangesAsync(HttpContext.RequestAborted);
        }

        return Ok();
    }

    /// <summary>
    /// Reset user password
    /// </summary>
    /// <remarks>
    /// Use this API to reset user password, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully retrieved</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">User not found</response>
    [HttpDelete("Users/{userid:guid}/Password")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(string userid)
    {
        var user = await userManager.FindByIdAsync(userid);

        if (user is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_UserNotFound)],
                StatusCodes.Status404NotFound));

        var actor = await userManager.GetUserAsync(User);
        if (actor is null)
            return Unauthorized();

        if (!RolePolicy.CanManageRole(actor.Role, user.Role))
            return Forbid();

        var pwd = Codec.RandomPassword(16);
        var code = await userManager.GeneratePasswordResetTokenAsync(user);
        await userManager.ResetPasswordAsync(user, code, pwd);

        return Ok(pwd);
    }

    /// <summary>
    /// Delete user
    /// </summary>
    /// <remarks>
    /// Use this API to delete user, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully retrieved</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">User not found</response>
    [HttpDelete("Users/{userid:guid}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(Guid userid, CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);

        if (user!.Id == userid)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Admin_SelfDeletionNotAllowed)]));

        user = await userManager.FindByIdAsync(userid.ToString());

        if (user is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_UserNotFound)],
                StatusCodes.Status404NotFound));

        var actor = await userManager.GetUserAsync(User);
        if (actor is null)
            return Unauthorized();

        if (!RolePolicy.CanManageRole(actor.Role, user.Role))
            return Forbid();

        if (user.Role == Role.SuperAdmin && await userManager.Users.CountAsync(u => u.Role == Role.SuperAdmin, token) <= 1)
            return BadRequest(new RequestResponse("不能删除最后一个超级管理员。"));

        if (await teamRepository.CheckIsCaptain(user, token))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Admin_CaptainDeletionNotAllowed)]));

        await userManager.DeleteAsync(user);

        return Ok();
    }

    /// <summary>
    /// Delete team
    /// </summary>
    /// <remarks>
    /// Use this API to delete team, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully retrieved</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">User not found</response>
    [HttpDelete("Teams/{id:int}")]
    [RequireAdmin]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTeam(int id, CancellationToken token = default)
    {
        var team = await teamRepository.GetTeamById(id, token);

        if (team is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Team_NotFound)],
                StatusCodes.Status404NotFound));

        await teamRepository.DeleteTeam(team, token);

        return Ok();
    }

    /// <summary>
    /// Get user information
    /// </summary>
    /// <remarks>
    /// Use this API to get user information, requires Admin permission
    /// </remarks>
    /// <response code="200">User object</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Users/{userid:guid}")]
    [ProducesResponseType(typeof(ProfileUserInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UserInfo(string userid)
    {
        var user = await userManager.FindByIdAsync(userid);

        if (user is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_UserNotFound)],
                StatusCodes.Status404NotFound));

        var actor = await userManager.GetUserAsync(User);
        if (actor is null)
            return Unauthorized();

        if (!RolePolicy.CanViewRole(actor.Role, user.Role))
            return Forbid();

        return Ok(ProfileUserInfoModel.FromUserInfo(user));
    }

    /// <summary>
    /// Get all logs
    /// </summary>
    /// <remarks>
    /// Use this API to get all logs, requires Admin permission
    /// </remarks>
    /// <response code="200">Log list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Logs")]
    [RequireAdmin]
    [ProducesResponseType(typeof(LogMessageModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logs([FromQuery] string? level = "All",
        [FromQuery][Range(0, 1000)] int count = 50,
        [FromQuery] int skip = 0, CancellationToken token = default) =>
        Ok(await logRepository.GetLogs(skip, count, level, token));

    /// <summary>
    /// Update participation status
    /// </summary>
    /// <remarks>
    /// Use this API to update team participation status, review application, requires Admin permission
    /// </remarks>
    /// <response code="200">Update successful</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Participation object not found</response>
    [HttpPut("Participation/{id:int}")]
    [RequireAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Participation(int id, [FromBody] ParticipationEditModel model,
        CancellationToken token = default)
    {
        await using var transaction = await participationRepository.BeginTransactionAsync(token);

        var participation = await participationRepository.GetParticipationById(id, token);

        if (participation is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_ParticipationNotFound)],
                StatusCodes.Status404NotFound));

        await participationRepository.UpdateParticipation(participation, model, token);

        await transaction.CommitAsync(token);
        await cacheHelper.FlushScoreboardCache(participation.GameId, token);

        return Ok();
    }

    /// <summary>
    /// Get all Writeup basic information
    /// </summary>
    /// <remarks>
    /// Use this API to get Writeup basic information, requires Admin permission
    /// </remarks>
    /// <response code="200">Update successful</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Game not found</response>
    [HttpGet("Writeups/{id:int}")]
    [RequireAdmin]
    [ProducesResponseType(typeof(WriteupInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Writeups(int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(await participationRepository.GetWriteups(game, token));
    }

    /// <summary>
    /// Download all Writeups
    /// </summary>
    /// <remarks>
    /// Use this API to download all Writeups, requires Admin permission
    /// </remarks>
    /// <response code="200">Downloaded successfully</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Game not found</response>
    [HttpGet("Writeups/{id:int}/All")]
    [RequireAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAllWriteups(int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var into = await participationRepository.GetWriteups(game, token);
        var filename = $"Writeups-{game.Title}-{DateTimeOffset.UtcNow:yyyyMMdd-HH.mm.ss}Z";

        return new TarFilesResult(storage, into.Writeups.Select(p => p.File), PathHelper.Uploads, filename, token);
    }

    /// <summary>
    /// Get all container instances
    /// </summary>
    /// <remarks>
    /// Use this API to get all container instances, requires Admin permission
    /// </remarks>
    /// <response code="200">Instance list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Instances")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ArrayResponse<ContainerInstanceModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Instances(CancellationToken token = default) =>
        Ok(new ArrayResponse<ContainerInstanceModel>(await containerRepository.GetContainerInstances(token)));

    /// <summary>
    /// Delete container instance
    /// </summary>
    /// <remarks>
    /// Use this API to forcibly delete container instance, requires Admin permission
    /// </remarks>
    /// <response code="200">Successfully retrieved</response>
    /// <response code="400">Container instance destruction failed</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    /// <response code="404">Container instance not found</response>
    [HttpDelete("Instances/{id:guid}")]
    [RequireAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [SuppressMessage("ReSharper", "RouteTemplates.ParameterTypeCanBeMadeStricter")]
    public async Task<IActionResult> DestroyInstance(Guid id, CancellationToken token = default)
    {
        var container = await containerRepository.GetContainerById(id, token);

        if (container is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Admin_ContainerInstanceNotFound)],
                StatusCodes.Status404NotFound));

        if (await containerRepository.DestroyContainer(container, token))
            return Ok();

        return BadRequest(
            new RequestResponse(localizer[nameof(Resources.Program.Admin_ContainerInstanceDestroyFailed)]));
    }

    /// <summary>
    /// Get all files
    /// </summary>
    /// <remarks>
    /// Use this API to get all files, requires Admin permission
    /// </remarks>
    /// <response code="200">File list</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Forbidden</response>
    [HttpGet("Files")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ArrayResponse<LocalFile>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Files([FromQuery][Range(0, 500)] int count = 50, [FromQuery] int skip = 0,
        CancellationToken token = default) =>
        Ok(new ArrayResponse<LocalFile>(await blobService.GetBlobs(count, skip, token)));

    private IActionResult HandleIdentityError(IEnumerable<IdentityError> errors) =>
        BadRequest(new RequestResponse(errors.FirstOrDefault()?.Description ??
                                       localizer[nameof(Resources.Program.Identity_UnknownError)]));

    private IQueryable<UserInfo> FilterVisibleUsers(UserInfo actor, IQueryable<UserInfo> query, int? groupId = null)
    {
        var roles = RolePolicy.ViewableRoles(actor.Role);
        query = query.Where(u => roles.Contains(u.Role));

        if (actor.Role < Role.Admin)
        {
            var visibleStudentIds = context.StudentGroupMembers
                .Where(m => context.StudentGroupManagers.Any(gm => gm.GroupId == m.GroupId && gm.ManagerId == actor.Id))
                .Select(m => m.StudentId);
            query = query.Where(u => visibleStudentIds.Contains(u.Id));
        }

        if (groupId.HasValue)
        {
            query = query.Where(u => context.StudentGroupMembers.Any(m => m.GroupId == groupId.Value && m.StudentId == u.Id));
        }

        return query;
    }

    private async Task<bool> CanManageStudentGroup(UserInfo actor, int groupId, CancellationToken token) =>
        actor.Role >= Role.Admin ||
        await context.StudentGroupManagers.AnyAsync(m => m.GroupId == groupId && m.ManagerId == actor.Id, token);

    private async Task<bool> CanSyncStudentGroups(UserInfo actor, UserInfo target, List<int>? groupIds, CancellationToken token)
    {
        if (target.Role != Role.Student || groupIds is null)
            return true;

        foreach (var groupId in groupIds.Distinct())
            if (!await CanManageStudentGroup(actor, groupId, token))
                return false;

        return true;
    }

    private async Task<List<int>?> ResolveStudentGroupsForCreatedUser(
        UserInfo actor,
        Role requestedRole,
        List<int>? groupIds,
        CancellationToken token)
    {
        if (requestedRole != Role.Student || actor.Role >= Role.Admin || groupIds is { Count: > 0 })
            return groupIds;

        var group = await context.StudentGroups
            .Include(g => g.Managers)
            .FirstOrDefaultAsync(g => g.CreatedById == actor.Id && g.Name == "我的默认分组" && !g.IsArchived, token);

        if (group is null)
        {
            group = new StudentGroup
            {
                Name = "我的默认分组",
                Description = "系统为老师创建学生时自动维护的默认培训分组。",
                CreatedById = actor.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            group.Managers.Add(new StudentGroupManager
            {
                Group = group,
                ManagerId = actor.Id,
                RoleInGroup = StudentGroupManagerRole.Owner,
                AddedById = actor.Id
            });
            context.StudentGroups.Add(group);
            await context.SaveChangesAsync(token);
        }
        else if (group.Managers.All(m => m.ManagerId != actor.Id))
        {
            context.StudentGroupManagers.Add(new StudentGroupManager
            {
                GroupId = group.Id,
                ManagerId = actor.Id,
                RoleInGroup = StudentGroupManagerRole.Owner,
                AddedById = actor.Id
            });
            await context.SaveChangesAsync(token);
        }

        return [group.Id];
    }

    private async Task SyncStudentGroups(UserInfo actor, UserInfo target, List<int>? groupIds, CancellationToken token)
    {
        if (target.Role != Role.Student || groupIds is null)
            return;

        var targetIds = groupIds.Distinct().ToArray();

        var memberships = await context.StudentGroupMembers
            .Where(m => m.StudentId == target.Id)
            .ToArrayAsync(token);
        var manageableIds = actor.Role >= Role.Admin
            ? memberships.Select(m => m.GroupId).ToHashSet()
            : await context.StudentGroupManagers
                .Where(gm => gm.ManagerId == actor.Id && memberships.Select(m => m.GroupId).Contains(gm.GroupId))
                .Select(gm => gm.GroupId)
                .ToHashSetAsync(token);
        var removable = actor.Role >= Role.Admin
            ? memberships
            : memberships.Where(m => manageableIds.Contains(m.GroupId)).ToArray();

        context.StudentGroupMembers.RemoveRange(removable.Where(m => !targetIds.Contains(m.GroupId)));

        var existingIds = memberships.Select(m => m.GroupId).ToHashSet();
        foreach (var groupId in targetIds.Where(groupId => !existingIds.Contains(groupId)))
        {
            context.StudentGroupMembers.Add(new StudentGroupMember
            {
                GroupId = groupId,
                StudentId = target.Id,
                AddedById = actor.Id
            });
        }
    }

    private async Task<Dictionary<Guid, List<UserStudentGroupModel>>> GetUserGroups(Guid[] userIds, CancellationToken token)
    {
        var memberships = await context.StudentGroupMembers
            .Include(m => m.Group)
            .Where(m => userIds.Contains(m.StudentId))
            .ToArrayAsync(token);

        return memberships
            .GroupBy(m => m.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => new UserStudentGroupModel { Id = m.GroupId, Name = m.Group.Name }).ToList());
    }

    private static UserInfoModel FillUserGroups(UserInfoModel model, Dictionary<Guid, List<UserStudentGroupModel>> groups)
    {
        if (model.Id is { } id && groups.TryGetValue(id, out var userGroups))
            model.StudentGroups = userGroups;

        return model;
    }
}
