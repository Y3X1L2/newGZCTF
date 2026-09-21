using GZCTF.Middlewares;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.League.Api;

[ApiController]
[RequireUser]
[Route("api/league/matches")]
public sealed class LeagueMatchesController(LeagueMatchService matches, UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet]
    public async Task<LeagueMatchPage> List([FromQuery] Guid? after = null, [FromQuery] int limit = 30, CancellationToken ct = default) =>
        await matches.ListAsync(await ActorAsync(), after, limit, ct);

    [HttpGet("{matchId:guid}")]
    public async Task<LeagueMatchDetail> Get(Guid matchId, CancellationToken ct) => await matches.GetAsync(matchId, await ActorAsync(), ct);

    [HttpPost, RequireAdmin]
    public async Task<ActionResult<LeagueMatchDetail>> Create(LeagueDraftModel model, CancellationToken ct)
    {
        var result = await matches.CreateAsync(model, await ActorAsync(), ct);
        return CreatedAtAction(nameof(Get), new { matchId = result.Match.Id }, result);
    }

    [HttpPut("{matchId:guid}"), RequireAdmin]
    public async Task<LeagueMatchDetail> Update(Guid matchId, LeagueUpdateDraftModel model, CancellationToken ct) =>
        await matches.UpdateAsync(matchId, model, await ActorAsync(), ct);

    [HttpPost("{matchId:guid}/registrations")]
    public async Task<LeagueMatchDetail> Register(Guid matchId, LeagueRegisterModel model, CancellationToken ct) =>
        await matches.RegisterAsync(matchId, model.TeamId, await ActorAsync(), ct);

    [HttpPost("{matchId:guid}/registrations/{teamId:int}/review"), RequireAdmin]
    public async Task<LeagueMatchDetail> Review(Guid matchId, int teamId, LeagueReviewModel model, CancellationToken ct) =>
        await matches.ReviewAsync(matchId, teamId, model.State, await ActorAsync(), ct);

    [HttpPost("{matchId:guid}/teams"), RequireAdmin]
    public async Task<LeagueMatchDetail> SelectTeams(Guid matchId, LeagueSelectTeamsModel model, CancellationToken ct) =>
        await matches.SelectTeamsAsync(matchId, model, await ActorAsync(), ct);

    [HttpPost("{matchId:guid}/prepare"), RequireAdmin]
    public async Task<ActionResult<LeagueMatchDetail>> Prepare(Guid matchId, LeagueRevisionModel model, CancellationToken ct) =>
        Accepted(await matches.PrepareAsync(matchId, model.Revision, await ActorAsync(), ct));

    [HttpPost("{matchId:guid}/start"), RequireAdmin]
    public async Task<ActionResult<LeagueMatchDetail>> Start(Guid matchId, CancellationToken ct) =>
        Accepted(await matches.StartAsync(matchId, await ActorAsync(), ct));

    [HttpPost("{matchId:guid}/abort"), RequireAdmin]
    public async Task<LeagueMatchDetail> Abort(Guid matchId, LeagueAbortModel model, CancellationToken ct) =>
        await matches.AbortAsync(matchId, model.Reason, await ActorAsync(), ct);

    [HttpPost("{matchId:guid}/retry"), RequireAdmin]
    public async Task<ActionResult<LeagueMatchDetail>> Retry(Guid matchId, CancellationToken ct) =>
        Accepted(await matches.RetryAsync(matchId, false, await ActorAsync(), ct));

    [HttpPost("{matchId:guid}/cleanup/retry"), RequireAdmin]
    public async Task<ActionResult<LeagueMatchDetail>> RetryCleanup(Guid matchId, CancellationToken ct) =>
        Accepted(await matches.RetryAsync(matchId, true, await ActorAsync(), ct));

    private async Task<ActorContext> ActorAsync()
    {
        var user = await users.GetUserAsync(User) ?? throw new LeagueException("league_login_required", "请先登录。", 401);
        return new(user.Id, user.Role);
    }
}
