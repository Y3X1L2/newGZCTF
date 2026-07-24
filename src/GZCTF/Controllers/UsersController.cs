using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Middlewares;
using GZCTF.Models.Request.Info;
using GZCTF.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

/// <summary>
/// Public user profile and private personal overview APIs.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class UsersController(UserProfileQueryService profiles, UserManager<UserInfo> userManager) :
    ControllerBase
{
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(PublicUserProfileModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> Profile(Guid userId, CancellationToken token)
    {
        var profile = await profiles.GetProfileAsync(userId, token);
        if (profile is null)
            return NotFound(new RequestResponse("User does not exist.", StatusCodes.Status404NotFound));

        if (IsNotModified($"{profile.Id:N}|{profile.UserName}|{profile.Role}|{profile.Bio}|{profile.Avatar}|" +
                          string.Join(',', profile.TaughtCourses.Select(item => item.Id))))
            return StatusCode(StatusCodes.Status304NotModified);

        return Ok(profile);
    }

    [HttpGet("{userId:guid}/overview")]
    [ProducesResponseType(typeof(UserProfileOverviewModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> Overview(Guid userId, [FromQuery] string window = "365d",
        CancellationToken token = default)
    {
        if (!UserProfileQueryService.TryResolveWindow(window, out var normalized, out _))
            return BadRequest(new RequestResponse("Unsupported profile window."));

        var overview = await profiles.GetOverviewAsync(userId, normalized, token);
        if (overview is null)
            return NotFound(new RequestResponse("User does not exist.", StatusCodes.Status404NotFound));

        if (IsNotModified($"{userId:N}|{overview.Window}|{overview.GeneratedAt.UtcDateTime.Ticks}"))
            return StatusCode(StatusCodes.Status304NotModified);

        return Ok(overview);
    }

    [HttpGet("{userId:guid}/activity")]
    [ProducesResponseType(typeof(UserActivityPointModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activity(Guid userId, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken token)
    {
        if (from == default || to == default || from > to || to.DayNumber - from.DayNumber > 366)
            return BadRequest(new RequestResponse("Activity range must contain at most 367 days."));

        var activity = await profiles.GetActivityAsync(userId, from, to, token);
        return activity is null
            ? NotFound(new RequestResponse("User does not exist.", StatusCodes.Status404NotFound))
            : Ok(activity);
    }

    [HttpGet("{userId:guid}/history")]
    [ProducesResponseType(typeof(UserProfileHistoryPageModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History(Guid userId, [FromQuery] string? type = "all",
        [FromQuery] string? cursor = null, [FromQuery] int count = 20, CancellationToken token = default)
    {
        if (!UserProfileQueryService.IsHistoryTypeSupported(type))
            return BadRequest(new RequestResponse("Unsupported profile history type."));
        count = Math.Clamp(count, 1, 50);

        var history = await profiles.GetHistoryAsync(userId, type, cursor, count, token);
        return history is null
            ? NotFound(new RequestResponse("User does not exist.", StatusCodes.Status404NotFound))
            : Ok(history);
    }

    [HttpGet("me/private-overview")]
    [RequireUser]
    [ProducesResponseType(typeof(UserPrivateOverviewModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PrivateOverview(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        return user is null
            ? Unauthorized(new RequestResponse("Login required.", StatusCodes.Status401Unauthorized))
            : Ok(await profiles.GetPrivateOverviewAsync(user.Id, token));
    }

    private bool IsNotModified(string source)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
        var eTag = $"\"user-{digest}\"";
        Response.Headers.ETag = eTag;
        Response.Headers.CacheControl = "no-cache, must-revalidate";
        return Request.Headers.IfNoneMatch.Any(value => (value ?? string.Empty).Split(',')
            .Select(item => item.Trim())
            .Any(item => item is "*" || string.Equals(item, eTag, StringComparison.Ordinal)));
    }
}
