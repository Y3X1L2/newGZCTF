using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

public sealed record GuacamoleRemoteSession(string ConnectionId, string UserId, string ConnectUrl);

public sealed class GuacamoleUnavailableException() : Exception("Guacamole administration is unavailable.") { }

public sealed class GuacamoleRemoteSessionService(
    IHttpClientFactory clients,
    GuacamoleService guacamole,
    IOptions<GuacamoleSettings> options,
    ILogger<GuacamoleRemoteSessionService> logger)
{
    private readonly GuacamoleSettings _settings = options.Value;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task EnsureAvailableAsync(CancellationToken token)
    {
        if (await guacamole.GetAuthTokenAsync(token) is null)
            throw new GuacamoleUnavailableException();
    }

    public Task<GuacamoleRemoteSession> CreateVncAsync(Guid sessionId, string host, int port, CancellationToken token) =>
        CreateAsync(sessionId, "vnc", host, port, string.Empty, string.Empty, token);

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
            ?? throw new GuacamoleUnavailableException();
        var client = clients.CreateClient("GuacamoleClient");
        var suffix = sessionId.ToString("N");
        var temporaryUser = "tlops_" + suffix;
        var temporaryPassword = RandomSecret();
        string? connectionId = null;
        string? userId = null;
        try
        {
            var connectionName = "tlops-" + suffix;
            var connection = protocol switch
            {
                "rdp" => GuacamoleService.BuildRdpConnectionData(connectionName, host, port, username, password),
                "ssh" => GuacamoleService.BuildSshConnectionData(connectionName, host, port, username, password),
                "vnc" => GuacamoleService.BuildVncConnectionData(connectionName, host, port),
                _ => throw new InvalidOperationException("Unsupported remote protocol.")
            };
            connectionId = await CreateConnectionAsync(client, adminToken, connectionName, connection, cancellationToken);
            userId = await CreateUserAsync(client, adminToken, temporaryUser, temporaryPassword, cancellationToken);
            await GrantConnectionAsync(client, adminToken, userId, connectionId, cancellationToken);
            var userToken = await LoginAsync(client, temporaryUser, temporaryPassword, cancellationToken);
            return new GuacamoleRemoteSession(connectionId, userId, BuildConnectUrl(connectionId, userToken));
        }
        catch
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await DeleteSessionAsync(sessionId, connectionId, userId, cleanupTimeout.Token); }
            catch { logger.LogWarning("Guacamole creation compensation pending for session {SessionId}", sessionId); }
            throw;
        }
    }

    public async Task DeleteSessionAsync(Guid sessionId, string? connectionId, string? userId, CancellationToken cancellationToken)
    {
        // Deterministic names recover resources when creation succeeded but its response was lost.
        userId ??= "tlops_" + sessionId.ToString("N");
        var cleanupFailed = false;
        try
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                var token = await guacamole.GetAuthTokenAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Guacamole cleanup authentication is unavailable.");
                var client = clients.CreateClient("GuacamoleClient");
                using var response = await client.GetAsync($"{_settings.GuacamoleApiUrl}/session/data/postgresql/connections?token={Uri.EscapeDataString(token)}", cancellationToken);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Guacamole cleanup inventory is unavailable.");
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var name = "tlops-" + sessionId.ToString("N");
                foreach (var item in document.RootElement.EnumerateObject())
                {
                    if (item.Value.TryGetProperty("name", out var resourceName) && resourceName.GetString() == name)
                    {
                        try { await DeleteAsync(item.Name, null, cancellationToken); }
                        catch (Exception) when (!cancellationToken.IsCancellationRequested) { cleanupFailed = true; }
                    }
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { cleanupFailed = true; }
        // Revoke the temporary identity even if inventory or one connection deletion failed.
        await DeleteAsync(connectionId, userId, cancellationToken);
        if (cleanupFailed) throw new InvalidOperationException("Guacamole resource cleanup is incomplete.");
    }

    public async Task DeleteAsync(string? connectionId, string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionId) && string.IsNullOrWhiteSpace(userId)) return;
        var adminToken = await guacamole.GetAuthTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("Guacamole cleanup authentication is unavailable.");
        var client = clients.CreateClient("GuacamoleClient");
        List<Exception> errors = [];
        if (!string.IsNullOrWhiteSpace(userId))
        {
            try { await DeleteUserAsync(client, adminToken, userId, cancellationToken); }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested) { errors.Add(exception); }
        }
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            try { await DeleteResourceAsync(client, adminToken, "connections", connectionId, cancellationToken); }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested) { errors.Add(exception); }
        }
        if (errors.Count != 0) throw new InvalidOperationException("Guacamole resource cleanup is incomplete.");
    }

    private async Task<string> CreateConnectionAsync(HttpClient client, string token, string name, object data, CancellationToken ct)
    {
        using var response = await client.PostAsync($"{_settings.GuacamoleApiUrl}/session/data/postgresql/connections?token={Uri.EscapeDataString(token)}",
            new StringContent(JsonSerializer.Serialize(data, Json), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("identifier").GetString() ?? throw new InvalidOperationException("Guacamole did not return a connection identifier.");
    }

    private async Task<string> CreateUserAsync(HttpClient client, string token, string username, string password, CancellationToken ct)
    {
        using var response = await client.PostAsync($"{_settings.GuacamoleApiUrl}/session/data/postgresql/users?token={Uri.EscapeDataString(token)}",
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
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            $"{_settings.GuacamoleApiUrl}/session/data/postgresql/users/{Uri.EscapeDataString(userId)}/permissions?token={Uri.EscapeDataString(token)}")
        {
            Content = new StringContent(JsonSerializer.Serialize(operations, Json), Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> LoginAsync(HttpClient client, string username, string password, CancellationToken ct)
    {
        using var response = await client.PostAsync($"{_settings.GuacamoleApiUrl}/tokens",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = password }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("authToken").GetString() ?? throw new InvalidOperationException("Guacamole temporary login failed.");
    }

    private async Task DeleteUserAsync(HttpClient client, string token, string userId, CancellationToken ct)
    {
        await DeleteResourceAsync(client, token, "users", userId, ct);
    }

    private async Task DeleteResourceAsync(HttpClient client, string token, string kind, string id, CancellationToken ct)
    {
        using var response = await client.DeleteAsync($"{_settings.GuacamoleApiUrl}/session/data/postgresql/{kind}/{Uri.EscapeDataString(id)}?token={Uri.EscapeDataString(token)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Guacamole {ResourceKind} cleanup returned HTTP {StatusCode}", kind, (int)response.StatusCode);
            throw new InvalidOperationException("Guacamole resource deletion was rejected.");
        }
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
