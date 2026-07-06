using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Container.Manager;

namespace GZCTF.Services.Fleet;

public class AgentClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentClient> _logger;

    public AgentClient(IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<AgentClient> logger)
    { _httpClientFactory = httpClientFactory; _scopeFactory = scopeFactory; _config = config; _logger = logger; }

    private HttpClient BuildClient(WorkerNode node)
    {
        var client = _httpClientFactory.CreateClient("Agent");
        client.BaseAddress = new Uri($"http://{node.HostAddress}:{node.AgentPort}");
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", node.AuthToken);
        return client;
    }

    private async Task<WorkerNode?> GetNodeAsync(Guid nodeId, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        return await repo.GetNodeByIdAsync(nodeId, token);
    }

    public virtual async Task<AgentCreateContainerResponse?> CreateContainerAsync(Guid nodeId, ContainerConfig config, CancellationToken token)
    {
        try
        {
            return await CreateContainerOrThrowAsync(nodeId, config, token);
        }
        catch (Exception ex) when (ex is AgentClientException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Agent create container failed on node {NodeId} for image {Image}",
                nodeId, config.Image);
            return null;
        }
    }

    public virtual async Task<AgentCreateContainerResponse> CreateContainerOrThrowAsync(Guid nodeId, ContainerConfig config,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new AgentClientException($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new
        {
            config.Image, config.TeamId, config.ChallengeId, config.UserId,
            config.ExposedPort, config.Flag, config.EnableTrafficCapture,
            config.MemoryLimit, config.CPUCount, config.StorageLimit,
            NetworkMode = config.NetworkMode.ToString(),
            config.NetworkName,
            config.IPAddress,
            config.AdditionalNetworkNames,
            config.NetworkSubnets,
            config.NetworkAttachments,
            config.PublishPort,
            config.PreferredHostPort,
            config.BypassPublicProxy,
            config.EnvironmentVariables,
            config.StartCommand,
            config.DnsServers,
            config.HealthCheck,
            config.UsePenetrationFabric,
            config.UseHostNetworkNone,
            config.EnableNetworkAdmin,
            config.RemoveDefaultRoute,
            config.EnableIpForwarding
        });
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync("/api/containers/create",
                new StringContent(body, Encoding.UTF8, "application/json"), token);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new AgentClientException(
                $"Agent request timed out on node {node.Name} ({node.HostAddress}) while creating image {config.Image}.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AgentClientException(
                $"Agent request failed on node {node.Name} ({node.HostAddress}) while creating image {config.Image}: {ex.Message}",
                ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new AgentClientException(
                $"Agent create container failed on node {node.Name} ({node.HostAddress}) for image {config.Image}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }

        var result = await response.Content.ReadFromJsonAsync<AgentCreateContainerResponse>(token);
        return result ?? throw new AgentClientException(
            $"Agent returned an empty container response on node {node.Name} ({node.HostAddress}) for image {config.Image}.");
    }

    public virtual async Task DestroyContainerAsync(Guid nodeId, string containerId, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new InvalidOperationException($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/containers/{Uri.EscapeDataString(containerId)}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Agent container deletion failed on node {nodeId}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }
    }

    public virtual async Task<AgentCommandResult> ExecuteContainerCommandAsync(Guid nodeId, string containerId,
        IReadOnlyList<string> command, int timeoutSeconds, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return AgentCommandResult.Unsupported($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new
        {
            command,
            timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 60)
        });
        var response = await client.PostAsync($"/api/containers/{Uri.EscapeDataString(containerId)}/exec",
            new StringContent(body, Encoding.UTF8, "application/json"), token);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            return AgentCommandResult.Failed(null,
                $"Agent command failed: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }

        var result = await response.Content.ReadFromJsonAsync<AgentCommandResult>(token);
        return result ?? AgentCommandResult.Failed(null, "Agent returned an empty command result.");
    }

    public async Task RemoveNetworkAsync(Guid nodeId, string networkName, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new InvalidOperationException($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/containers/networks/{Uri.EscapeDataString(networkName)}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Agent network deletion failed on node {nodeId}, network {networkName}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }
    }

    public async Task<TeamLabStatusResponse?> GetTeamLabStatusAsync(Guid nodeId, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return null;

        var client = BuildClient(node);
        var response = await client.GetAsync("/api/teamlab/status", token);
        return await ReadTeamLabResponseAsync<TeamLabStatusResponse>(response, token);
    }

    public virtual async Task<TeamLabDryRunResponse?> CreateTeamLabBridgeAsync(Guid nodeId, TeamLabBridgeRequest request,
        CancellationToken token) => await PostTeamLabAsync<TeamLabBridgeRequest, TeamLabDryRunResponse>(nodeId,
        "/api/teamlab/bridges", request, token);

    public virtual async Task<TeamLabDryRunResponse?> CreateTeamLabRouterAsync(Guid nodeId, TeamLabRouterRequest request,
        CancellationToken token) => await PostTeamLabAsync<TeamLabRouterRequest, TeamLabDryRunResponse>(nodeId,
        "/api/teamlab/routers", request, token);

    public virtual async Task<TeamLabDryRunResponse?> ConfigureTeamLabWireGuardAsync(Guid nodeId,
        TeamLabWireGuardRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabWireGuardRequest, TeamLabDryRunResponse>(nodeId,
            "/api/teamlab/wireguard", request, token);

    public virtual async Task<TeamLabDryRunResponse?> CleanupTeamLabAsync(Guid nodeId, TeamLabCleanupRequest request,
        CancellationToken token) => await PostTeamLabAsync<TeamLabCleanupRequest, TeamLabDryRunResponse>(nodeId,
        "/api/teamlab/cleanup", request, token);

    public virtual async Task<TeamLabDryRunResponse?> ProbeTeamLabAsync(Guid nodeId, TeamLabProbeRequest request,
        CancellationToken token) => await PostTeamLabAsync<TeamLabProbeRequest, TeamLabDryRunResponse>(nodeId,
        "/api/teamlab/probe", request, token);

    public virtual async Task<TeamLabDryRunResponse?> AttachTeamLabContainerAsync(Guid nodeId,
        TeamLabContainerAttachRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabContainerAttachRequest, TeamLabDryRunResponse>(nodeId,
            "/api/teamlab/containers/attach", request, token);

    public virtual async Task<TeamLabDryRunResponse?> ConfigureTeamLabDhcpDnsAsync(Guid nodeId,
        TeamLabDhcpDnsRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabDhcpDnsRequest, TeamLabDryRunResponse>(nodeId,
            "/api/teamlab/dhcp-dns", request, token);

    public virtual async Task<TeamLabDryRunResponse?> ProbeTeamLabDhcpDnsAsync(Guid nodeId,
        TeamLabDhcpDnsProbeRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabDhcpDnsProbeRequest, TeamLabDryRunResponse>(nodeId,
            "/api/teamlab/dhcp-dns/probe", request, token);

    private async Task<TResponse?> PostTeamLabAsync<TRequest, TResponse>(Guid nodeId, string path, TRequest request,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return default;

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(request);
        var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), token);
        return await ReadTeamLabResponseAsync<TResponse>(response, token);
    }

    private async Task<T?> ReadTeamLabResponseAsync<T>(HttpResponseMessage response, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            _logger.LogWarning("Agent TeamLab request failed: {Status}. Body: {Body}",
                response.StatusCode, TrimResponseBody(responseBody));
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(token);
    }

    public async Task<PenetrationFabricResult> CreateFabricNetworkAsync(Guid nodeId, string networkName, string cidr,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        return await PostFabricAsync(client, "/api/containers/fabric/networks",
            new { networkName, cidr }, token);
    }

    public async Task<PenetrationFabricResult> AttachFabricInterfaceAsync(Guid nodeId, string containerId,
        PenetrationFabricInterfaceSpec spec, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        return await PostFabricAsync(client,
            $"/api/containers/{Uri.EscapeDataString(containerId)}/fabric/interfaces",
            new
            {
                spec.NetworkName,
                NetworkCidr = spec.NetworkCidr,
                spec.HostInterfaceName,
                spec.ContainerInterfaceName,
                spec.IpAddress,
                spec.PrefixLength,
                spec.IsPrimary,
                spec.RemoveDefaultRoute
            }, token);
    }

    public async Task<PenetrationFabricResult> EnableFabricForwardingAsync(Guid nodeId, string containerId,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        return await PostFabricAsync(client,
            $"/api/containers/{Uri.EscapeDataString(containerId)}/fabric/forwarding",
            new { }, token);
    }

    public async Task<PenetrationFabricResult> ApplyFabricRouteAsync(Guid nodeId, string containerId,
        string targetCidr, string gatewayIp, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        return await PostFabricAsync(client,
            $"/api/containers/{Uri.EscapeDataString(containerId)}/fabric/routes",
            new { targetCidr, gatewayIp }, token);
    }

    public async Task<PenetrationFabricResult> ProbeFabricAsync(Guid nodeId, string containerId, string targetIp,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        return await PostFabricAsync(client,
            $"/api/containers/{Uri.EscapeDataString(containerId)}/fabric/probe",
            new { targetIp }, token);
    }

    public async Task<PenetrationFabricResult> RemoveFabricNetworkAsync(Guid nodeId, string networkName,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/containers/fabric/networks/{Uri.EscapeDataString(networkName)}", token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            return PenetrationFabricResult.Failed(null,
                $"Agent fabric network deletion failed: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }

        var result = await response.Content.ReadFromJsonAsync<PenetrationFabricResult>(token);
        return result ?? PenetrationFabricResult.Failed(null, "Agent returned an empty fabric result.");
    }

    static async Task<PenetrationFabricResult> PostFabricAsync(HttpClient client, string path, object body,
        CancellationToken token)
    {
        var response = await client.PostAsync(path,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            return PenetrationFabricResult.Failed(null,
                $"Agent fabric command failed: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }

        var result = await response.Content.ReadFromJsonAsync<PenetrationFabricResult>(token);
        return result ?? PenetrationFabricResult.Failed(null, "Agent returned an empty fabric result.");
    }

    public virtual async Task<AgentCreateVmResponse?> CreateVmAsync(Guid nodeId, AgentCreateVmRequest request, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return null;

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(request);
        var response = await client.PostAsync("/api/vms/create", new StringContent(body, Encoding.UTF8, "application/json"), token);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Agent create VM failed on node {NodeId}: {Status}", nodeId, response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<AgentCreateVmResponse>(token);
    }

    public virtual async Task DestroyVmAsync(Guid nodeId, string vmName, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new InvalidOperationException($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/vms/{Uri.EscapeDataString(vmName)}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Agent VM deletion failed on node {nodeId}, VM {vmName}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }
    }

    public async Task<AgentVmIpResponse?> GetVmIpAsync(Guid nodeId, string vmName, CancellationToken token) =>
        await GetVmIpAsync(nodeId, vmName, [], token);

    public async Task<AgentVmIpResponse?> GetVmIpAsync(Guid nodeId, string vmName,
        IReadOnlyList<AgentVmNetworkInterfaceRequest> interfaces, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return null;

        var client = BuildClient(node);
        try
        {
            var path = $"/api/vms/{Uri.EscapeDataString(vmName)}/ip";
            var response = interfaces.Count == 0
                ? await client.GetAsync(path, token)
                : await client.PostAsync(path,
                    new StringContent(JsonSerializer.Serialize(new { interfaces }), Encoding.UTF8,
                        "application/json"), token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Agent VM IP lookup failed on node {NodeId}: {Status}",
                    nodeId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AgentVmIpResponse>(token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent VM IP lookup failed on node {NodeId}", nodeId);
            return null;
        }
    }

    public async Task PullDockerImageAsync(Guid nodeId, string image, string? registryAuth, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return;

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new { image, registryAuth });
        var response = await client.PostAsync("/api/images/pull-docker",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            _logger.LogWarning("Agent Docker image pull failed on node {NodeId}: {Status}. Body: {Body}",
                nodeId, response.StatusCode, TrimResponseBody(responseBody));
        }
    }

    public async Task EnsureDockerRegistryAsync(Guid nodeId, int port, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new InvalidOperationException($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new { port = Math.Clamp(port, 1, 65535) });
        var response = await client.PostAsync("/api/images/ensure-docker-registry",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Agent Docker registry bootstrap failed on node {nodeId}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }
    }

    public async Task ConfigureDockerRegistryAsync(Guid nodeId, string registry, CancellationToken token)
    {
        await ConfigureDockerRegistriesAsync(nodeId, [registry], token);
    }

    public async Task ConfigureDockerRegistriesAsync(Guid nodeId, IReadOnlyCollection<string> registries,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new InvalidOperationException($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new { registries });
        var response = await client.PostAsync("/api/images/configure-docker-registry",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Agent Docker registry trust configuration failed on node {nodeId}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }
    }

    public async Task DownloadVmImageAsync(Guid nodeId, int templateId, string hash, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return;

        var client = BuildClient(node);
        var serverUrl = NodeDeployService.ResolveServerUrl(_config);
        var body = JsonSerializer.Serialize(new
        {
            templateId,
            hash,
            downloadUrl = $"{serverUrl.TrimEnd('/')}/api/v1/image-templates/download/{hash}?nodeId={nodeId}",
            authToken = node.AuthToken
        });
        var response = await client.PostAsync("/api/images/download-vm",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            _logger.LogWarning("Agent VM image download failed on node {NodeId}: {Status}. Body: {Body}",
                nodeId, response.StatusCode, TrimResponseBody(responseBody));
        }
    }

    static string TrimResponseBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        body = body.Trim();
        return body.Length <= 2048 ? body : body[..2048] + "...";
    }
}

public class AgentCreateContainerResponse
{
    public string ContainerId { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int PublicPort { get; set; }
}

public class AgentClientException : Exception
{
    public AgentClientException(string message) : base(message)
    {
    }

    public AgentClientException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class AgentCreateVmRequest
{
    public int? TemplateId { get; set; }
    public string? TemplatePath { get; set; }
    public string VmName { get; set; } = string.Empty;
    public int Memory { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    public string? Flag { get; set; }
    public List<AgentVmNetworkInterfaceRequest> Interfaces { get; set; } = [];
}

public class AgentVmNetworkInterfaceRequest
{
    public string BridgeName { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public string Model { get; set; } = "e1000e";
}

public class AgentCreateVmResponse
{
    public string VmName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? VncAddress { get; set; }
    public List<AgentVmNetworkInterfaceRequest> Interfaces { get; set; } = [];
}

public class AgentCommandResult
{
    public bool IsSupported { get; set; } = true;
    public bool Succeeded { get; set; }
    public bool TimedOut { get; set; }
    public long? ExitCode { get; set; }
    public string? Message { get; set; }

    public static AgentCommandResult Failed(long? exitCode, string? message) => new()
    {
        Succeeded = false,
        TimedOut = false,
        ExitCode = exitCode,
        Message = message
    };

    public static AgentCommandResult Unsupported(string? message) => new()
    {
        IsSupported = false,
        Succeeded = false,
        Message = message
    };
}

public class AgentVmIpResponse
{
    public string VmName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int? RdpPort { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Diagnostic { get; set; }
}

public record TeamLabStatusResponse(
    bool Available,
    bool Enable,
    bool DryRun,
    bool HasIpCommand,
    bool HasWireGuardCommand,
    bool HasIptablesCommand,
    DateTimeOffset CheckedAt,
    string? Message = null);

public record TeamLabDryRunResponse(
    bool Success,
    bool DryRun,
    string Message,
    string[] Commands);

public record TeamLabBridgeRequest(
    int RuntimeId,
    string BridgeName,
    string Cidr,
    bool DryRun = true);

public record TeamLabRouterInterfaceRequest(
    string BridgeName,
    string GatewayAddressCidr);

public record TeamLabStaticRouteRequest(
    string TargetCidr,
    string GatewayIp);

public record TeamLabRouterRequest(
    int RuntimeId,
    string NamespaceName,
    TeamLabRouterInterfaceRequest[] Interfaces,
    TeamLabStaticRouteRequest[] Routes,
    bool DryRun = true);

public record TeamLabWireGuardRequest(
    int RuntimeId,
    string NamespaceName,
    string InterfaceName,
    int ListenPort,
    string AddressCidr,
    string InterfacePrivateKey,
    string PeerPublicKey,
    string PeerClientAddress,
    string PeerAllowedIps,
    string[] PlayerAllowedCidrs,
    string[] PlayerBlockedCidrs,
    bool DryRun = true);

public record TeamLabCleanupRequest(
    int RuntimeId,
    string[] ResourceNames,
    bool DryRun = true);

public record TeamLabProbeRequest(
    int RuntimeId,
    string NamespaceName,
    string TargetIp,
    bool DryRun = true);

public record TeamLabContainerAttachRequest(
    int RuntimeId,
    string ContainerId,
    string BridgeName,
    string HostInterfaceName,
    string ContainerInterfaceName,
    string AddressCidr,
    string? MacAddress,
    bool RemoveDefaultRoute,
    string? GatewayIp,
    string[] StaticRoutes,
    string[] DnsServers,
    bool DryRun = true);

public record TeamLabDhcpLeaseRequest(
    string MacAddress,
    string IpAddress,
    string Hostname);

public record TeamLabDnsRecordRequest(
    string Hostname,
    string IpAddress);

public record TeamLabDhcpDnsRequest(
    int RuntimeId,
    string ServiceName,
    string NamespaceName,
    string BridgeName,
    string InterfaceName,
    string GatewayIp,
    string Cidr,
    string Domain,
    TeamLabDhcpLeaseRequest[] Leases,
    TeamLabDnsRecordRequest[] DnsRecords,
    bool DryRun = true);

public record TeamLabDhcpDnsProbeRequest(
    int RuntimeId,
    string NamespaceName,
    string GatewayIp,
    string Hostname,
    bool DryRun = true);
