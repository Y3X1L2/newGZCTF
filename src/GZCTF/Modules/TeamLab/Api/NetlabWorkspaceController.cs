using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[RequireUser]
[ApiController]
[Route("api/teamlab")]
public sealed class NetlabWorkspaceController(UserManager<UserInfo> users, NetlabConnection connection,
    NetlabWorkspaceAccess access, NetlabProxy proxy, NetlabClient client) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace(int? gameId, string? section, string? environmentId, string? runtimeId, CancellationToken token)
    {
        var user = (await users.GetUserAsync(User))!;
        var workspace = await access.AuthorizeAsync(user, gameId, token);
        if (!connection.Configured) return Ok(new { available = false, error = "尚未连接组网底座" });
        var id = workspace.EnvironmentId ?? environmentId ?? runtimeId;
        string route;
        if (!string.IsNullOrEmpty(id))
        {
            await client.GetAsync<Infrastructure.Netlab.Environment>("environments/" + Uri.EscapeDataString(id), token, workspace.Scope);
            route = "/environments/" + Uri.EscapeDataString(id);
        }
        else route = section switch { "templates" => "/templates", "resources" => "/resources", _ => "/environments" };
        return Ok(new { available = true, frameUrl = workspace.BasePath + route });
    }

    [RequireAdmin]
    [HttpGet("connection")]
    public NetlabConnectionInfo Connection() => connection.Info();

    [RequireAdmin]
    [HttpPut("connection")]
    public Task<NetlabConnectionInfo> Connect(NetlabConnectionSettings input, CancellationToken token) => connection.SaveAsync(input, token);

    [Route("netlab/admin/{**path}")]
    [DisableRequestSizeLimit]
    public Task AdminProxy(string? path, CancellationToken token) => ForwardAsync(null, path, token);

    [Route("netlab/game/{gameId:int}/{**path}")]
    [DisableRequestSizeLimit]
    public Task GameProxy(int gameId, string? path, CancellationToken token) => ForwardAsync(gameId, path, token);

    private async Task ForwardAsync(int? gameId, string? path, CancellationToken token)
    {
        var user = (await users.GetUserAsync(User))!;
        var workspace = await access.AuthorizeAsync(user, gameId, token);
        await proxy.ForwardAsync(HttpContext, workspace, path ?? "");
    }
}
