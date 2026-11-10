using System.Net.Http.Headers;
using System.Text.Json;
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

    public HttpRequestMessage Request(HttpMethod method, string path, RequestScope? scope = null)
    {
        if (!Configured) throw new TeamLabApiContractException("netlab_not_connected", "请先连接组网底座。", 503);
        var root = Endpoint;
        var target = new Uri(root, path);
        if (!root.IsBaseOf(target))
            throw new TeamLabApiContractException("invalid_path", "请求路径无效。", 400);
        var request = new HttpRequestMessage(method, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _protector.Unprotect(configuration["Netlab:Credential"]!));
        if (scope is not null)
            request.Headers.Add("X-Netlab-Context", Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(NetlabClient.Serialize(scope))));
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
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(root), "api/v1/capabilities"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        request.Headers.Add("X-Netlab-Context", Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            System.Text.Encoding.UTF8.GetBytes(NetlabClient.Serialize(new RequestScope { Name = "连接检查", Grants = [] }))));
        using var response = await clients.CreateClient("Netlab").SendAsync(request, token);
        await NetlabClient.EnsureSuccessAsync(response, token);
        var capabilities = await response.Content.ReadFromJsonAsync<Capabilities>(NetlabClient.Json, token);
        if (capabilities?.WorkspaceProtocol != 1)
            throw new TeamLabApiContractException("workspace_protocol_required", "请更新底座以支持工作区接入。", 400);
        await configs.SaveConfigSet([
            new("Netlab:Endpoint", root), new("Netlab:ProjectId", input.ProjectName.Trim()),
            new("Netlab:Credential", _protector.Protect(credential))
        ], token);
        return new(root, input.ProjectName.Trim(), true);
    }
}
