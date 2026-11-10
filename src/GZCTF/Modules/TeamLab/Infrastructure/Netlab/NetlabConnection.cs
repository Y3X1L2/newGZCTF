using System.Net.Http.Headers;
using GZCTF.Services.Config;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.AspNetCore.DataProtection;

namespace GZCTF.Modules.TeamLab.Infrastructure.Netlab;

public sealed record NetlabConnectionSettings(string Endpoint, string ProjectName, string? Token = null);
public sealed record NetlabConnectionInfo(string Endpoint, string ProjectName, bool Configured);

public sealed class NetlabConnection(IConfiguration configuration, IConfigService configs,
    IDataProtectionProvider protection, IHttpClientFactory clients)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Netlab.ServiceCredential");
    public Uri Endpoint => new(configuration["Netlab:Endpoint"]!);
    public string ProjectId => configuration["Netlab:ProjectId"] ?? "";
    public bool Configured => !string.IsNullOrEmpty(configuration["Netlab:Endpoint"]) &&
                              !string.IsNullOrEmpty(configuration["Netlab:Credential"]) && ProjectId.Length > 0;

    public NetlabConnectionInfo Info() => new(configuration["Netlab:Endpoint"] ?? "", ProjectId, Configured);

    public HttpRequestMessage Request(HttpMethod method, string path)
    {
        if (!Configured) throw new TeamLabApiContractException("netlab_not_connected", "请先连接组网底座。", 503);
        var root = Endpoint;
        var target = new Uri(root, path);
        if (!root.IsBaseOf(target))
            throw new TeamLabApiContractException("invalid_path", "请求路径无效。", 400);
        var request = new HttpRequestMessage(method, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _protector.Unprotect(configuration["Netlab:Credential"]!));
        return request;
    }

    public async Task<NetlabConnectionInfo> SaveAsync(NetlabConnectionSettings input, CancellationToken token)
    {
        if (!Uri.TryCreate(input.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length > 0 ||
            endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0 || string.IsNullOrWhiteSpace(input.ProjectName))
            throw new TeamLabApiContractException("invalid_connection", "请填写底座 HTTP 地址和项目名称。", 400);
        var credential = input.Token ?? (Configured ? _protector.Unprotect(configuration["Netlab:Credential"]!) : "");
        var root = endpoint.AbsoluteUri.TrimEnd('/') + "/";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(root), "api/v1/identity"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        using var response = await clients.CreateClient("Netlab").SendAsync(request, token);
        await NetlabClient.EnsureSuccessAsync(response, token);
        var identity = Newtonsoft.Json.JsonConvert.DeserializeObject<Identity>(await response.Content.ReadAsStringAsync(token))!;
        if (!(identity.Grants ?? []).Any(grant => grant.ScopeKind == ScopeGrantScopeKind.Project && grant.ScopeId == input.ProjectName.Trim()))
            throw new TeamLabApiContractException("project_not_granted", "请填写此服务 Token 已授权的项目名称。", 400);
        await configs.SaveConfigSet([
            new("Netlab:Endpoint", root), new("Netlab:ProjectId", input.ProjectName.Trim()),
            new("Netlab:Credential", _protector.Protect(credential))
        ], token);
        return new(root, input.ProjectName.Trim(), true);
    }
}
