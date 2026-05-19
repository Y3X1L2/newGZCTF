using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

/// <summary>
/// Wraps the Apache Guacamole REST API for creating and managing web-based
/// RDP connections to Windows target VMs in CTF scenarios.
/// </summary>
public class GuacamoleProxy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GuacamoleProxy> _logger;
    private readonly string _apiUrl;
    private readonly string _authToken;
    private readonly int _timeoutSeconds;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new instance of <see cref="GuacamoleProxy"/> with typed HttpClient
    /// and Guacamole configuration from the "GuacamoleSettings" configuration section.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating HttpClient instances.</param>
    /// <param name="settings">Guacamole configuration options.</param>
    /// <param name="logger">Structured logger for API call auditing.</param>
    public GuacamoleProxy(
        IHttpClientFactory httpClientFactory,
        IOptions<GuacamoleSettings> settings,
        ILogger<GuacamoleProxy> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        var cfg = settings.Value;
        _apiUrl = string.IsNullOrWhiteSpace(cfg.GuacamoleApiUrl)
            ? "http://localhost:8081/guacamole/api"
            : cfg.GuacamoleApiUrl.TrimEnd('/');
        _authToken = cfg.GuacamoleAuthToken ?? string.Empty;
        _timeoutSeconds = cfg.ConnectionTimeoutSeconds > 0 ? cfg.ConnectionTimeoutSeconds : 10;
    }

    /// <summary>
    /// Creates a new RDP connection in Guacamole for the specified VM and returns
    /// the connection identifier and authentication token for browser access.
    /// </summary>
    /// <param name="vmName">Display name for the connection (typically the VM name).</param>
    /// <param name="host">Target host IP address or hostname running the RDP service.</param>
    /// <param name="port">Target RDP port (usually 3389).</param>
    /// <returns>A tuple of (connectionId, token) for constructing the browser URL.</returns>
    /// <exception cref="GuacamoleApiException">Thrown when the Guacamole API call fails.</exception>
    public async Task<(string ConnectionId, string Token)> CreateConnectionAsync(
        string vmName, string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmName);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        _logger.LogInformation("Creating Guacamole RDP connection for '{VmName}' at {Host}:{Port}",
            vmName, host, port);

        var payload = new
        {
            parentIdentifier = "ROOT",
            name = vmName,
            protocol = "rdp",
            parameters = new
            {
                hostname = host,
                port,
                username = "player",
                password = "password",
                ignoreCert = "true",
                security = "any",
                enableWallpaper = "false",
                enableTheming = "false",
                enableFontSmoothing = "true",
                enableFullWindowDrag = "false",
                enableDesktopComposition = "false",
                enableMenuAnimations = "false",
                disableAudio = "true",
                serverLayout = "en-us-qwerty"
            }
        };

        var client = CreateHttpClient();
        var request = CreateRequest(HttpMethod.Post, $"{_apiUrl}/connections", payload);
        var response = await client.SendAsync(request);

        await EnsureSuccessOrThrowAsync(response, "create connection");

        var connection = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var connectionId = connection.GetProperty("identifier").GetString()
                           ?? throw new GuacamoleApiException("Connection ID not found in Guacamole response.");

        _logger.LogDebug("Guacamole connection '{ConnectionId}' created, generating auth token", connectionId);

        // Generate a one-time use token for this connection
        var token = await GenerateTokenAsync(connectionId, client);

        _logger.LogInformation("Guacamole connection '{ConnectionId}' created for '{VmName}'",
            connectionId, vmName);

        return (connectionId, token);
    }

    /// <summary>
    /// Creates a new RDP connection in Guacamole with dynamic credentials.
    /// </summary>
    /// <param name="vmName">Display name for the connection.</param>
    /// <param name="host">Target host IP address or hostname.</param>
    /// <param name="port">Target RDP port (usually 3389).</param>
    /// <param name="username">RDP login username.</param>
    /// <param name="password">RDP login password.</param>
    /// <returns>A tuple of (connectionId, token) for constructing the browser URL.</returns>
    /// <exception cref="GuacamoleApiException">Thrown when the Guacamole API call fails.</exception>
    public async Task<(string ConnectionId, string Token)> CreateConnectionWithCredentialsAsync(
        string vmName, string host, int port, string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmName);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        _logger.LogInformation("Creating Guacamole RDP connection for '{VmName}' at {Host}:{Port} with user '{User}'",
            vmName, host, port, username);

        var payload = new
        {
            parentIdentifier = "ROOT",
            name = vmName,
            protocol = "rdp",
            parameters = new
            {
                hostname = host,
                port,
                username,
                password,
                ignoreCert = "true",
                security = "any",
                enableWallpaper = "false",
                enableTheming = "false",
                enableFontSmoothing = "true",
                enableFullWindowDrag = "false",
                enableDesktopComposition = "false",
                enableMenuAnimations = "false",
                disableAudio = "true",
                serverLayout = "en-us-qwerty"
            }
        };

        var client = CreateHttpClient();
        var request = CreateRequest(HttpMethod.Post, $"{_apiUrl}/connections", payload);
        var response = await client.SendAsync(request);

        await EnsureSuccessOrThrowAsync(response, "create connection");

        var connection = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var connectionId = connection.GetProperty("identifier").GetString()
                           ?? throw new GuacamoleApiException("Connection ID not found in Guacamole response.");

        _logger.LogDebug("Guacamole connection '{ConnectionId}' created, generating auth token", connectionId);

        var token = await GenerateTokenAsync(connectionId, client);

        _logger.LogInformation("Guacamole connection '{ConnectionId}' created for '{VmName}'",
            connectionId, vmName);

        return (connectionId, token);
    }

    /// <summary>
    /// Deletes a Guacamole connection by its identifier.
    /// </summary>
    /// <param name="connectionId">The Guacamole connection identifier to delete.</param>
    /// <exception cref="GuacamoleApiException">Thrown when the API call fails.</exception>
    public async Task DeleteConnectionAsync(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        _logger.LogInformation("Deleting Guacamole connection '{ConnectionId}'", connectionId);

        var client = CreateHttpClient();
        var request = CreateRequest(HttpMethod.Delete, $"{_apiUrl}/connections/{connectionId}");
        var response = await client.SendAsync(request);

        await EnsureSuccessOrThrowAsync(response, "delete connection");

        _logger.LogInformation("Guacamole connection '{ConnectionId}' deleted", connectionId);
    }

    /// <summary>
    /// Builds the browser-accessible URL for connecting to a VM through the Guacamole web client.
    /// </summary>
    /// <param name="connectionId">The Guacamole connection identifier.</param>
    /// <param name="token">The authentication token generated during connection creation.</param>
    /// <returns>A relative URL path for the Guacamole web client.</returns>
    public string GetConnectionUrl(string connectionId, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var url = $"/guacamole/#/client/{Uri.EscapeDataString(connectionId)}?token={Uri.EscapeDataString(token)}";

        _logger.LogDebug("Generated Guacamole URL for connection '{ConnectionId}'", connectionId);
        return url;
    }

    /// <summary>
    /// Generates a one-time authentication token for accessing a specific connection.
    /// </summary>
    private async Task<string> GenerateTokenAsync(string connectionId, HttpClient client)
    {
        var payload = new
        {
            connections = new[]
            {
                new
                {
                    identifier = connectionId,
                    parameters = new { }
                }
            }
        };

        var request = CreateRequest(HttpMethod.Post, $"{_apiUrl}/tokens", payload);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Guacamole token for connection '{ConnectionId}'", connectionId);
            throw new GuacamoleApiException($"Failed to generate auth token: {ex.Message}", ex);
        }

        await EnsureSuccessOrThrowAsync(response, "generate token");

        var tokenResponse = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = tokenResponse.GetProperty("authToken").GetString()
                    ?? throw new GuacamoleApiException("Auth token not found in Guacamole response.");

        return token;
    }

    /// <summary>
    /// Creates a configured HttpClient with the appropriate timeout.
    /// </summary>
    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("GuacamoleClient");
        client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
        return client;
    }

    /// <summary>
    /// Creates an HttpRequestMessage with optional body payload and Guacamole auth header.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (!string.IsNullOrWhiteSpace(_authToken))
            request.Headers.Add("Guacamole-Token", _authToken);

        if (body is not null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

        return request;
    }

    /// <summary>
    /// Validates the HTTP response and throws a <see cref="GuacamoleApiException"/> on failure.
    /// </summary>
    private async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogError("Guacamole API {Operation} failed (HTTP {StatusCode}): {ResponseBody}",
            operation, (int)response.StatusCode, body);

        throw new GuacamoleApiException(
            $"Guacamole API {operation} failed with status {(int)response.StatusCode}: {body}");
    }
}

/// <summary>
/// Exception thrown when a Guacamole API operation fails.
/// </summary>
public class GuacamoleApiException : Exception
{
    /// <summary>
    /// Creates a new <see cref="GuacamoleApiException"/> with the specified error message.
    /// </summary>
    public GuacamoleApiException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="GuacamoleApiException"/> with the specified error message and inner exception.
    /// </summary>
    public GuacamoleApiException(string message, Exception innerException) : base(message, innerException) { }
}
