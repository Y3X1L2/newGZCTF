using GZCTF.Middlewares;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Newtonsoft.Json;
using NetlabEnvironment = GZCTF.Modules.TeamLab.Infrastructure.Netlab.Environment;
using NetlabIdentity = GZCTF.Modules.TeamLab.Infrastructure.Netlab.Identity;

namespace GZCTF.Modules.TeamLab.Api;

[RequireUser]
[ApiController]
[Route("api/teamlab")]
public sealed class NetlabWorkspaceController(UserManager<UserInfo> users, NetlabConnection connection,
    NetlabWorkspaceAccess access, NetlabProxy proxy, NetlabClient client, IDistributedLeaseProvider locks) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace(int? gameId, string? section, string? environmentId, string? runtimeId, CancellationToken token)
    {
        var workspace = await access.AuthorizeAsync((await users.GetUserAsync(User))!, gameId, token);
        if (!connection.Configured) return Ok(new { available = false, error = "尚未连接组网底座" });
        return Ok(new { available = true, environmentId = workspace.EnvironmentId ?? environmentId ?? runtimeId });
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
        path = NetlabProxy.ValidateRequest(Request, path ?? "");
        var parts = path[7..].Split('/');
        await using var entryLease = gameId is { } game && HttpMethods.IsPost(Request.Method) &&
            parts is ["environments", _, "vpn-access"] or ["environments", _, "assets", _, "services"]
            ? await locks.AcquireAsync(NetlabWorkspaceAccess.GameLeaseKey(game), TimeSpan.FromSeconds(10), cancellationToken: token) : null;
        using var lifetime = entryLease is null ? null : CancellationTokenSource.CreateLinkedTokenSource(token, entryLease.LeaseLost);
        token = lifetime?.Token ?? token;
        var user = (await users.GetUserAsync(User))!;
        var workspace = await access.AuthorizeAsync(user, gameId, token);
        await access.RunSessionAsync(user, gameId, workspace, async session =>
        {
            await AuthorizeResourceAsync(workspace, parts, session);
            var result = await HandleAsync(workspace, parts, session);
            if (result is not null)
            {
                Response.ContentType = "application/json";
                await Response.WriteAsync(NetlabClient.Serialize(result), session);
            }
            else await proxy.ForwardAsync(HttpContext, workspace, path, session);
        }, token);
    }

    private async Task AuthorizeResourceAsync(NetlabWorkspaceContext workspace, string[] path, CancellationToken token)
    {
        var read = HttpMethods.IsGet(Request.Method) || HttpMethods.IsHead(Request.Method);
        switch (path[0])
        {
            case "identity" when path.Length == 1 && read:
            case "templates" when read && (workspace.EnvironmentId is null || path.Length == 1):
                return;
            case "environments" when path.Length > 1 && path[1] != "summary":
                if (workspace.EnvironmentId is { } assigned)
                {
                    if (path[1] != assigned || !PlayerOperation(path, read)) throw NetlabWorkspaceAccess.Forbidden();
                }
                else if (path.Length != 2 || !HttpMethods.IsGet(Request.Method))
                    await access.EnvironmentAsync(workspace, path[1], token);
                if (path.Last() == "grants") throw NetlabWorkspaceAccess.Forbidden();
                return;
            case "operations":
                var id = path.Length == 1 ? Request.Query["environmentId"].ToString() :
                    (await client.GetAsync<Operation>($"operations/{Uri.EscapeDataString(path[1])}", token)).EnvironmentId;
                if (!string.IsNullOrEmpty(id))
                {
                    if (workspace.EnvironmentId is { } own && id != own) throw NetlabWorkspaceAccess.Forbidden();
                    if (workspace.EnvironmentId is null) await access.EnvironmentAsync(workspace, id, token);
                }
                else if (path.Length > 1 && !workspace.Administrator) throw NetlabWorkspaceAccess.Forbidden();
                if (workspace.EnvironmentId is not null && (!read || id != workspace.EnvironmentId)) throw NetlabWorkspaceAccess.Forbidden();
                return;
            case "blueprints" when workspace.EnvironmentId is null:
                if (path.Length > 1) await access.BlueprintAsync(workspace, path[1], token);
                return;
            case "blueprint-versions" when workspace.EnvironmentId is null && read && path.Length == 2:
                var version = await client.GetAsync<BlueprintVersion>($"blueprint-versions/{Uri.EscapeDataString(path[1])}", token);
                await access.BlueprintAsync(workspace, version.BlueprintId, token);
                return;
            case "nodes" or "storage-pools" or "volumes" or "backup-repositories" when workspace.EnvironmentId is null:
                if (!workspace.Administrator && !(read && path is ["nodes"] or ["storage-pools"] or
                    ["backup-repositories"] or ["nodes", _, "interfaces"])) throw NetlabWorkspaceAccess.Forbidden();
                return;
            case "environments" when workspace.EnvironmentId is null:
            case "templates" when workspace.EnvironmentId is null && workspace.Administrator:
                return;
            default:
                throw NetlabWorkspaceAccess.Forbidden();
        }
    }

    private static bool PlayerOperation(string[] path, bool read)
    {
        if (path.Length == 2) return read;
        if (path[2] == "assets" && path.Length >= 5)
            return path[4] is "console" or "ssh" or "rdp" or "files" or "services" || read && path[4] == "logs";
        return path[2] is "vpn-access" or "services" or "captures" ||
            read && path[2] is "state" or "storage" or "metrics" or "traffic" or "packets" or "security" or "events";
    }

    private async Task<object?> HandleAsync(NetlabWorkspaceContext workspace, string[] path, CancellationToken token)
    {
        if (HttpMethods.IsPost(Request.Method) && path is ["environments"])
        {
            var input = await BodyAsync<CreateEnvironment>(token);
            input.ProjectId = workspace.ProjectId;
            input.ExternalReference = workspace.ExternalReference;
            if (input.BlueprintVersionId is { } versionId)
            {
                var version = await client.GetAsync<BlueprintVersion>($"blueprint-versions/{Uri.EscapeDataString(versionId)}", token);
                await access.BlueprintAsync(workspace, version.BlueprintId, token);
            }
            if (input.RecoveryPointId is not null || input.BackupId is not null)
            {
                input.AdditionalProperties.Remove("sourceEnvironmentId", out var sourceValue);
                var source = sourceValue?.ToString() ?? throw NetlabWorkspaceAccess.Forbidden();
                await access.EnvironmentAsync(workspace, source, token);
                var pathPrefix = $"environments/{Uri.EscapeDataString(source)}";
                var found = input.RecoveryPointId is { } point
                    ? (await client.ListAsync<RecoveryPointSummary>(pathPrefix + "/recovery-points", item => item.Id, token)).Any(item => item.Id == point)
                    : (await client.ListAsync<BackupSummary>(pathPrefix + "/backups", item => item.Id, token)).Any(item => item.Id == input.BackupId);
                if (!found) throw NetlabWorkspaceAccess.Forbidden();
            }
            var environment = await client.SendAsync<NetlabEnvironment>(HttpMethod.Post, "environments", input, token);
            Response.StatusCode = StatusCodes.Status201Created;
            Response.Headers.Location = workspace.BasePath + "/api/v1/environments/" + Uri.EscapeDataString(environment.Id);
            return environment;
        }
        if (HttpMethods.IsPost(Request.Method) && path is ["blueprints", var blueprintId, "versions"])
        {
            var input = await BodyAsync<SaveBlueprintVersion>(token);
            await access.EnvironmentAsync(workspace, input.EnvironmentId, token);
            var version = await client.SendAsync<BlueprintVersion>(HttpMethod.Post,
                $"blueprints/{Uri.EscapeDataString(blueprintId)}/versions", input, token);
            Response.StatusCode = StatusCodes.Status201Created;
            return version;
        }
        if (!HttpMethods.IsGet(Request.Method)) return null;
        if (path is ["identity"]) return await IdentityAsync(workspace, token);
        if (path is ["environments"] or ["environments", "summary"])
        {
            var rows = await access.EnvironmentsAsync(workspace, token);
            var query = Request.Query["search"].ToString();
            rows = rows.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.ExternalReference?.Contains(query, StringComparison.OrdinalIgnoreCase) == true).ToArray();
            if (path.Length == 2) return new { total = rows.Count, statuses = rows.GroupBy(item => item.Status.ToString().ToLowerInvariant()).ToDictionary(group => group.Key, group => group.Count()) };
            var status = Request.Query["status"].ToString();
            return Page(rows.Where(item => status.Length == 0 || string.Equals(item.Status.ToString(), status, StringComparison.OrdinalIgnoreCase)), item => item.Id);
        }
        if (path is ["environments", var id]) return await access.EnvironmentAsync(workspace, id, token);
        if (path is ["blueprints"]) return Page(await access.BlueprintsAsync(workspace, token), item => item.Id);
        if (path is ["templates"] && workspace.EnvironmentId is { } assigned)
        {
            var environment = await access.EnvironmentAsync(workspace, assigned, token);
            var ids = environment.Spec.Assets.Concat(environment.AppliedSpec?.Assets ?? []).Select(asset => asset.TemplateId).Distinct().ToArray();
            if (ids.Length == 0) return Array.Empty<Template>();
            return Page(await client.ListAsync<Template>("templates?ids=" + Uri.EscapeDataString(string.Join(',', ids)), item => item.Id, token), item => item.Id);
        }
        if (path is ["operations"] && workspace.EnvironmentId is null)
        {
            var ids = (await access.EnvironmentsAsync(workspace, token)).Select(item => item.Id).ToHashSet();
            var requested = Request.Query["environmentId"].ToString();
            var operations = await client.ListAsync<Operation>("operations?environmentId=" + Uri.EscapeDataString(requested), item => item.Id, token);
            return Page(operations.Where(item => item.EnvironmentId is not null && ids.Contains(item.EnvironmentId)), item => item.Id);
        }
        return null;
    }

    private async Task<NetlabIdentity> IdentityAsync(NetlabWorkspaceContext workspace, CancellationToken token)
    {
        var identity = await client.GetAsync<NetlabIdentity>("identity", token);
        identity.Name = workspace.Name;
        identity.Administrator &= workspace.Administrator;
        identity.Grants = (identity.Grants ?? []).Where(grant => grant.ScopeKind == ScopeGrantScopeKind.Project && grant.ScopeId == workspace.ProjectId).ToList();
        if (workspace.EnvironmentId is { } id)
            identity.Grants = [new ScopeGrant { ScopeKind = ScopeGrantScopeKind.Environment, ScopeId = id,
                Permissions = NetlabWorkspaceAccess.Limit(identity.Grants.SelectMany(grant => grant.Permissions)) }];
        return identity;
    }

    private async Task<T> BodyAsync<T>(CancellationToken token)
    {
        const long maximum = 16 * 1024 * 1024;
        if (Request.ContentLength > maximum)
            throw new TeamLabApiContractException("configuration_too_large", "环境配置超过 16 MiB。", 413);
        if (HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = maximum;
        using var reader = new StreamReader(Request.Body);
        try { return JsonConvert.DeserializeObject<T>(await reader.ReadToEndAsync(token))
            ?? throw new TeamLabApiContractException("invalid_configuration", "请提供环境配置对象。", 400); }
        catch (JsonException) { throw new TeamLabApiContractException("invalid_configuration", "环境配置格式无效。", 400); }
    }

    private T[] Page<T>(IEnumerable<T> source, Func<T, string> id)
    {
        var cursor = Request.Query["cursor"].ToString();
        var rows = cursor.Length == 0 ? source : source.SkipWhile(item => id(item) != cursor).Skip(1);
        var limit = int.TryParse(Request.Query["limit"], out var value) ? Math.Clamp(value, 1, 100) : 30;
        return rows.Take(limit).ToArray();
    }
}
