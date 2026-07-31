using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

public sealed record GuacamoleRemoteSession(string ConnectionId, string UserId, string ConnectUrl);

public sealed class GuacamoleRemoteSessionService(
    IHttpClientFactory clients,
    GuacamoleService guacamole,
    IOptions<GuacamoleSettings> options,
    ILogger<GuacamoleRemoteSessionService> logger)
{
    private readonly GuacamoleSettings _settings = options.Value;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<GuacamoleRemoteSession> CreateRdpAsync(
        Guid sessionId, string host, int port, string username, string password, CancellationToken cancellationToken)
        => await CreateAsync(sessionId, "rdp", host, port, username, password, cancellationToken);

    public async Task<GuacamoleRemoteSession> CreateSshAsync(
        Guid sessionId, string host, int port, string username, string password, CancellationToken cancellationToken)
        => await CreateAsync(sessionId, "ssh", host, port, username, password, cancellationToken);

    private async Task<GuacamoleRemoteSession> CreateAsync(
        Guid sessionId, string protocol, string host, int port, string username, string password, CancellationToken cancellationToken)
    {
        var adminToken = await guacamole.GetAuthTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("Guacamole administration is unavailable.");
        var client = clients.CreateClient("GuacamoleClient");
        var suffix = sessionId.ToString("N");
        var temporaryUser = "tlops_" + suffix;
        var temporaryPassword = RandomSecret();
        string? connectionId = null;
        string? userId = null;
        try
        {
            var connectionName = "tlops-" + suffix;
            var connection = protocol == "rdp"
                ? GuacamoleService.BuildRdpConnectionData(connectionName, host, port, username, password)
                : GuacamoleService.BuildSshConnectionData(connectionName, host, port, username, password);
            connectionId = await CreateConnectionAsync(client, adminToken, connectionName, connection, cancellationToken);
            userId = await CreateUserAsync(client, adminToken, temporaryUser, temporaryPassword, cancellationToken);
            await GrantConnectionAsync(client, adminToken, userId, connectionId, cancellationToken);
            var userToken = await LoginAsync(client, temporaryUser, temporaryPassword, cancellationToken);
            return new GuacamoleRemoteSession(connectionId, userId, BuildConnectUrl(connectionId, userToken));
        }
        catch
        {
            if (userId is not null) await DeleteUserAsync(client, adminToken, userId, CancellationToken.None);
            if (connectionId is not null) await guacamole.DeleteConnectionAsync(connectionId, CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteAsync(string? connectionId, string? userId, CancellationToken cancellationToken)
    {
        var adminToken = await guacamole.GetAuthTokenAsync(cancellationToken);
        if (adminToken is null) return;
        var client = clients.CreateClient("GuacamoleClient");
        if (!string.IsNullOrWhiteSpace(userId)) await DeleteUserAsync(client, adminToken, userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(connectionId)) await guacamole.DeleteConnectionAsync(connectionId, cancellationToken);
    }

    private async Task<string> CreateConnectionAsync(HttpClient client, string token, string name, object data, CancellationToken ct)
    {
        var response = await client.PostAsync($"{_settings.GuacamoleApiUrl}/session/data/postgresql/connections?token={Uri.EscapeDataString(token)}",
            new StringContent(JsonSerializer.Serialize(data, Json), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("identifier").GetString() ?? throw new InvalidOperationException("Guacamole did not return a connection identifier.");
    }

    private async Task<string> CreateUserAsync(HttpClient client, string token, string username, string password, CancellationToken ct)
    {
        var response = await client.PostAsync($"{_settings.GuacamoleApiUrl}/session/data/postgresql/users?token={Uri.EscapeDataString(token)}",
            new StringContent(JsonSerializer.Serialize(new { username, password, attributes = new Dictionary<string, string>() }, Json), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.TryGetProperty("identifier", out var identifier) &&
               !string.IsNullOrWhiteSpace(identifier.GetString())
            ? identifier.GetString()!
            : json.RootElement.TryGetProperty("username", out var createdUsername) &&
              !string.IsNullOrWhiteSpace(createdUsername.GetString())
                ? createdUsername.GetString()!
                : username;
    }

    private async Task GrantConnectionAsync(HttpClient client, string token, string userId, string connectionId, CancellationToken ct)
    {
        var operations = new[] { new { op = "add", path = $"/connectionPermissions/{connectionId}", value = "READ" } };
        var request = new HttpRequestMessage(HttpMethod.Patch,
            $"{_settings.GuacamoleApiUrl}/session/data/postgresql/users/{Uri.EscapeDataString(userId)}/permissions?token={Uri.EscapeDataString(token)}")
        {
            Content = new StringContent(JsonSerializer.Serialize(operations, Json), Encoding.UTF8, "application/json")
        };
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> LoginAsync(HttpClient client, string username, string password, CancellationToken ct)
    {
        var response = await client.PostAsync($"{_settings.GuacamoleApiUrl}/tokens",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = password }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("authToken").GetString() ?? throw new InvalidOperationException("Guacamole temporary login failed.");
    }

    private async Task DeleteUserAsync(HttpClient client, string token, string userId, CancellationToken ct)
    {
        try { await client.DeleteAsync($"{_settings.GuacamoleApiUrl}/session/data/postgresql/users/{Uri.EscapeDataString(userId)}?token={Uri.EscapeDataString(token)}", ct); }
        catch (Exception exception) { logger.LogWarning(exception, "Failed to delete temporary Guacamole user {UserId}", userId); }
    }

    private string BuildConnectUrl(string connectionId, string token)
    {
        var clientId = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connectionId}\0c\0postgresql"));
        var baseUrl = string.IsNullOrWhiteSpace(_settings.GuacamolePublicUrl)
            ? _settings.GuacamoleApiUrl.Replace("/api", string.Empty, StringComparison.OrdinalIgnoreCase)
            : _settings.GuacamolePublicUrl.TrimEnd('/');
        return $"{baseUrl}/#/client/{clientId}?token={Uri.EscapeDataString(token)}";
    }

    private static string RandomSecret()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}
