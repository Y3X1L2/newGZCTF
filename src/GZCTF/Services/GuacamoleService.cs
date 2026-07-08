using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

/// <summary>
/// Service for managing Apache Guacamole RDP connections via REST API.
/// </summary>
public class GuacamoleService
{
    private readonly HttpClient _httpClient;
    private readonly GuacamoleSettings _settings;
    private readonly ILogger<GuacamoleService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GuacamoleService(
        IHttpClientFactory httpClientFactory,
        IOptions<GuacamoleSettings> settings,
        ILogger<GuacamoleService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GuacamoleClient");
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates with Guacamole and returns an auth token.
    /// </summary>
    public async Task<string?> GetAuthTokenAsync(CancellationToken token = default)
    {
        // Return cached token if still valid
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedToken;

        await _tokenLock.WaitAsync(token);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
                return _cachedToken;

            // If pre-configured token exists, use it
            if (!string.IsNullOrEmpty(_settings.GuacamoleAuthToken))
            {
                _cachedToken = _settings.GuacamoleAuthToken;
                _tokenExpiry = DateTimeOffset.UtcNow.AddHours(1);
                return _cachedToken;
            }

            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = "guacadmin",
                    ["password"] = "guacadmin"
                });

                var response = await _httpClient.PostAsync(
                    $"{_settings.GuacamoleApiUrl}/tokens", content, token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Guacamole auth failed: {Status}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(token);
                using var doc = JsonDocument.Parse(json);
                _cachedToken = doc.RootElement.GetProperty("authToken").GetString();
                _tokenExpiry = DateTimeOffset.UtcNow.AddMinutes(50); // tokens last ~60min
                return _cachedToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to authenticate with Guacamole");
                return null;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Creates an RDP connection in Guacamole and returns the connection ID.
    /// </summary>
    public async Task<string?> CreateRdpConnectionAsync(
        string connectionName,
        string vmIp,
        int rdpPort = 3389,
        string username = "player",
        string password = "qwer1234!",
        CancellationToken token = default)
    {
        var authToken = await GetAuthTokenAsync(token);
        if (authToken is null) return null;

        try
        {
            var connectionData = BuildRdpConnectionData(connectionName, vmIp, rdpPort, username, password);
            var existingConnectionId = await FindConnectionIdByNameAsync(connectionName, authToken, token);
            if (!string.IsNullOrEmpty(existingConnectionId))
            {
                var updated = await UpdateConnectionAsync(existingConnectionId, connectionData, authToken, token);
                _logger.LogInformation(
                    "Reused existing Guacamole RDP connection '{Name}' (ID: {Id}, updated: {Updated}) for VM {Ip}",
                    connectionName, existingConnectionId, updated, vmIp);
                return existingConnectionId;
            }

            var json = JsonSerializer.Serialize(connectionData, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_settings.GuacamoleApiUrl}/session/data/postgresql/connections?token={authToken}",
                content, token);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(token);
                if (errBody.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    existingConnectionId = await FindConnectionIdByNameAsync(connectionName, authToken, token);
                    if (!string.IsNullOrEmpty(existingConnectionId))
                    {
                        var updated = await UpdateConnectionAsync(existingConnectionId, connectionData, authToken,
                            token);
                        _logger.LogInformation(
                            "Recovered duplicate Guacamole RDP connection '{Name}' (ID: {Id}, updated: {Updated}) for VM {Ip}",
                            connectionName, existingConnectionId, updated, vmIp);
                        return existingConnectionId;
                    }
                }

                _logger.LogError("Failed to create Guacamole connection: {Status} {Body}",
                    response.StatusCode, errBody);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(token);
            using var doc = JsonDocument.Parse(responseJson);
            var connectionId = doc.RootElement.GetProperty("identifier").GetString();

            _logger.LogInformation("Created Guacamole RDP connection '{Name}' (ID: {Id}) for VM {Ip}",
                connectionName, connectionId, vmIp);
            return connectionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Guacamole RDP connection for {Ip}", vmIp);
            return null;
        }
    }

    public static GuacamoleConnectionData BuildRdpConnectionData(
        string connectionName,
        string vmIp,
        int rdpPort,
        string username,
        string password) => new(
        connectionName,
        "ROOT",
        "rdp",
        new Dictionary<string, string>
        {
            ["hostname"] = vmIp,
            ["port"] = rdpPort.ToString(),
            ["username"] = username,
            ["password"] = password,
            ["security"] = "any",
            ["ignore-cert"] = "true",
            ["resize-method"] = "display-update",
            ["disable-clipboard"] = "false",
            ["enable-clipboard"] = "true",
            ["enable-wallpaper"] = "true",
            ["enable-theming"] = "false",
            ["enable-font-smoothing"] = "false",
            ["enable-full-window-drag"] = "false",
            ["enable-desktop-composition"] = "false",
            ["enable-menu-animations"] = "false",
            ["disable-bitmap-caching"] = "false",
            ["disable-glyph-caching"] = "false",
            ["color-depth"] = "16",
            ["width"] = "1280",
            ["height"] = "720"
        },
        new Dictionary<string, string>
        {
            ["max-connections"] = "2",
            ["max-connections-per-user"] = "2"
        });

    public sealed record GuacamoleConnectionData(
        string Name,
        string ParentIdentifier,
        string Protocol,
        Dictionary<string, string> Parameters,
        Dictionary<string, string> Attributes);

    private async Task<string?> FindConnectionIdByNameAsync(
        string connectionName,
        string authToken,
        CancellationToken token)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_settings.GuacamoleApiUrl}/session/data/postgresql/connections?token={authToken}",
                token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to list Guacamole connections: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(token);
            using var doc = JsonDocument.Parse(json);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var connection = property.Value;
                if (connection.ValueKind != JsonValueKind.Object)
                    continue;

                if (!connection.TryGetProperty("name", out var nameElement) ||
                    !string.Equals(nameElement.GetString(), connectionName, StringComparison.Ordinal))
                    continue;

                if (connection.TryGetProperty("identifier", out var identifierElement))
                    return identifierElement.GetString();

                return property.Name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find Guacamole connection by name {Name}", connectionName);
        }

        return null;
    }

    private async Task<bool> UpdateConnectionAsync(
        string connectionId,
        object connectionData,
        string authToken,
        CancellationToken token)
    {
        try
        {
            var json = JsonSerializer.Serialize(connectionData, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync(
                $"{_settings.GuacamoleApiUrl}/session/data/postgresql/connections/{connectionId}?token={authToken}",
                content,
                token);

            if (response.IsSuccessStatusCode)
                return true;

            var errBody = await response.Content.ReadAsStringAsync(token);
            _logger.LogWarning("Failed to update Guacamole connection {Id}: {Status} {Body}",
                connectionId, response.StatusCode, errBody);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error updating Guacamole connection {Id}", connectionId);
        }

        return false;
    }

    /// <summary>
    /// Deletes a Guacamole connection by ID.
    /// </summary>
    public virtual async Task<bool> DeleteConnectionAsync(string connectionId, CancellationToken token = default)
    {
        var authToken = await GetAuthTokenAsync(token);
        if (authToken is null) return false;

        try
        {
            var response = await _httpClient.DeleteAsync(
                $"{_settings.GuacamoleApiUrl}/session/data/postgresql/connections/{connectionId}?token={authToken}",
                token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Deleted Guacamole connection {Id}", connectionId);
                return true;
            }

            _logger.LogWarning("Failed to delete Guacamole connection {Id}: {Status}",
                connectionId, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Guacamole connection {Id}", connectionId);
            return false;
        }
    }

    /// <summary>
    /// Generates the full URL for a user to access an RDP session via Guacamole.
    /// The URL uses Guacamole's client path with a base64-encoded connection identifier.
    /// </summary>
    public string GetConnectionUrl(string connectionId)
    {
        // Guacamole client URL format: #/client/{base64(connectionId + \0 + c + \0 + postgresql)}
        var clientId = $"{connectionId}\0c\0postgresql";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId));
        var baseUrl = !string.IsNullOrEmpty(_settings.GuacamolePublicUrl)
            ? _settings.GuacamolePublicUrl.TrimEnd('/')
            : _settings.GuacamoleApiUrl.Replace("/api", "");
        return $"{baseUrl}/#/client/{encoded}";
    }

    /// <summary>
    /// Generates a URL with an embedded auth token so users can access RDP without logging in.
    /// </summary>
    public async Task<string?> GetAuthenticatedConnectionUrlAsync(string connectionId, CancellationToken token = default)
    {
        var authToken = await GetAuthTokenAsync(token);
        if (authToken is null) return null;

        var clientId = $"{connectionId}\0c\0postgresql";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId));
        var baseUrl = !string.IsNullOrEmpty(_settings.GuacamolePublicUrl)
            ? _settings.GuacamolePublicUrl.TrimEnd('/')
            : _settings.GuacamoleApiUrl.Replace("/api", "");
        return $"{baseUrl}/#/client/{encoded}?token={authToken}";
    }
}
