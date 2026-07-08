using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

public sealed class PortalSsoService(
    IHttpClientFactory httpClientFactory,
    IOptionsSnapshot<PortalSsoConfig> portalSsoConfig,
    ILogger<PortalSsoService> logger)
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PortalSsoProfileResult> GetProfileAsync(string portalToken, CancellationToken token = default)
    {
        var config = portalSsoConfig.Value;

        if (!config.Enabled)
            return PortalSsoProfileResult.Fail("Portal SSO is disabled.", StatusCodes.Status404NotFound);

        if (string.IsNullOrWhiteSpace(config.ProfileEndpoint) ||
            !Uri.TryCreate(config.ProfileEndpoint, UriKind.Absolute, out var endpoint))
            return PortalSsoProfileResult.Fail("Portal SSO profile endpoint is not configured.",
                StatusCodes.Status500InternalServerError);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 60)));

        var client = httpClientFactory.CreateClient("PortalSso");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", portalToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return PortalSsoProfileResult.Fail("Portal IAM profile request timed out.",
                StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Portal IAM profile request failed.");
            return PortalSsoProfileResult.Fail("Portal IAM profile request failed.",
                StatusCodes.Status502BadGateway);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);

        if (!response.IsSuccessStatusCode)
            return PortalSsoProfileResult.Fail($"Portal IAM rejected the token: {(int)response.StatusCode}.",
                StatusCodes.Status401Unauthorized);

        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            var root = document.RootElement;
            var profile = root.TryGetProperty("ok", out var okProp)
                ? ParseEnvelope(root)
                : root.Deserialize<PortalSsoProfile>(JsonOptions);

            if (profile?.User is null)
                return PortalSsoProfileResult.Fail("Portal IAM profile is missing user data.",
                    StatusCodes.Status401Unauthorized);

            if (profile.User.Id <= 0 || string.IsNullOrWhiteSpace(profile.User.RoleCode))
                return PortalSsoProfileResult.Fail("Portal IAM profile contains invalid user data.",
                    StatusCodes.Status401Unauthorized);

            if (config.RequireCtfPlatform &&
                !profile.Platforms.Any(p => string.Equals(p.Code, config.CtfPlatformCode,
                    StringComparison.OrdinalIgnoreCase)))
                return PortalSsoProfileResult.Fail("The portal user is not allowed to access the CTF platform.",
                    StatusCodes.Status403Forbidden);

            return PortalSsoProfileResult.Ok(profile);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Portal IAM profile response cannot be parsed.");
            return PortalSsoProfileResult.Fail("Portal IAM profile response cannot be parsed.",
                StatusCodes.Status502BadGateway);
        }
    }

    static PortalSsoProfile? ParseEnvelope(JsonElement root)
    {
        var ok = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
        if (!ok)
            return null;

        return root.TryGetProperty("data", out var data)
            ? data.Deserialize<PortalSsoProfile>(JsonOptions)
            : null;
    }
}

public sealed record PortalSsoProfileResult(
    bool Succeeded,
    PortalSsoProfile? Profile,
    string? Error,
    int StatusCode)
{
    public static PortalSsoProfileResult Ok(PortalSsoProfile profile) =>
        new(true, profile, null, StatusCodes.Status200OK);

    public static PortalSsoProfileResult Fail(string error, int statusCode) =>
        new(false, null, error, statusCode);
}

public sealed class PortalSsoProfile
{
    [JsonPropertyName("user")]
    public PortalSsoUser? User { get; set; }

    [JsonPropertyName("permissions")]
    public string[] Permissions { get; set; } = [];

    [JsonPropertyName("platforms")]
    public PortalSsoPlatform[] Platforms { get; set; } = [];
}

public sealed class PortalSsoUser
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("real_name")]
    public string RealName { get; set; } = string.Empty;

    [JsonPropertyName("role_code")]
    public string RoleCode { get; set; } = string.Empty;

    [JsonPropertyName("role_name")]
    public string RoleName { get; set; } = string.Empty;

    [JsonPropertyName("class_id")]
    public int? ClassId { get; set; }

    [JsonPropertyName("class_name")]
    public string? ClassName { get; set; }
}

public sealed class PortalSsoPlatform
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("entry_url")]
    public string EntryUrl { get; set; } = string.Empty;
}
