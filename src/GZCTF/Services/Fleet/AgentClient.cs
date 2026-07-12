using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net.Mime;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Container.Manager;
using Microsoft.EntityFrameworkCore;

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

    public virtual async Task<TeamLabDryRunResponse?> ApplyTeamLabFabricAsync(Guid nodeId,
        TeamLabFabricApplyRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabFabricApplyRequest, TeamLabDryRunResponse>(nodeId,
            "/api/teamlab/fabric/apply", request, token);

    public virtual async Task<TeamLabCaptureResponse?> StartTeamLabCaptureAsync(Guid nodeId,
        TeamLabCaptureStartRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabCaptureStartRequest, TeamLabCaptureResponse>(nodeId,
            "/api/teamlab/capture/start", request, token);

    public virtual async Task<TeamLabCaptureResponse?> StopTeamLabCaptureAsync(Guid nodeId,
        TeamLabCaptureStopRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabCaptureStopRequest, TeamLabCaptureResponse>(nodeId,
            "/api/teamlab/capture/stop", request, token);

    public virtual async Task<TeamLabCaptureResponse?> GetTeamLabCaptureStatusAsync(Guid nodeId,
        TeamLabCaptureStatusRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabCaptureStatusRequest, TeamLabCaptureResponse>(nodeId,
            "/api/teamlab/capture/status", request, token);

    public virtual async Task<TeamLabFlowResponse?> StartTeamLabFlowMetadataAsync(Guid nodeId,
        TeamLabFlowStartRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabFlowStartRequest, TeamLabFlowResponse>(nodeId,
            "/api/teamlab/flows/start", request, token);

    public virtual async Task<TeamLabFlowResponse?> StopTeamLabFlowMetadataAsync(Guid nodeId,
        TeamLabFlowStopRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabFlowStopRequest, TeamLabFlowResponse>(nodeId,
            "/api/teamlab/flows/stop", request, token);

    public virtual async Task<TeamLabFlowResponse?> GetTeamLabFlowMetadataSnapshotAsync(Guid nodeId,
        TeamLabFlowSnapshotRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabFlowSnapshotRequest, TeamLabFlowResponse>(nodeId,
            "/api/teamlab/flows/snapshot", request, token);

    public virtual async Task<AgentSyncResponse> SyncAgentAsync(Guid nodeId, AgentSyncRequest request,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return new AgentSyncResponse(false, $"Fleet node {nodeId} was not found.", null);

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(request);
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync("/api/maintenance/sync-agent",
                new StringContent(body, Encoding.UTF8, "application/json"), token);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new AgentClientException(
                $"Agent sync request timed out on node {node.Name} ({node.HostAddress}).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AgentClientException(
                $"Agent sync request failed on node {node.Name} ({node.HostAddress}): {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new AgentClientException(
                $"Agent sync failed on node {node.Name} ({node.HostAddress}): {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }

        return await response.Content.ReadFromJsonAsync<AgentSyncResponse>(token)
               ?? new AgentSyncResponse(false, "Agent returned an empty sync response.", null);
    }

    public virtual async Task<TeamLabCaptureDownloadResult?> DownloadTeamLabCaptureAsync(Guid nodeId,
        int runtimeId, int jobId, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return TeamLabCaptureDownloadResult.Failed($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var path = $"/api/teamlab/capture/{runtimeId}/{jobId}/download";
        var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            return TeamLabCaptureDownloadResult.Failed(
                $"Agent TeamLab capture download failed: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }

        var stream = await response.Content.ReadAsStreamAsync(token);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                       ?? $"teamlab-capture-{runtimeId}-{jobId}.pcap";
        var contentType = response.Content.Headers.ContentType?.ToString() ?? MediaTypeNames.Application.Octet;
        var length = response.Content.Headers.ContentLength;
        return TeamLabCaptureDownloadResult.FromStream(stream, fileName, contentType, length, response);
    }

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

        if (request.TemplateId.HasValue && !request.ImageEnsured)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var template = await context.ImageTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TemplateId.Value, token);
            if (template is null || string.IsNullOrWhiteSpace(template.ImageHash))
                throw new AgentClientException($"VM template {request.TemplateId.Value} is missing or has no image hash.");

            var download = await DownloadVmImageAsync(nodeId, template.Id, template.ImageHash, token: token);
            if (!download.Success)
                throw new AgentClientException(
                    $"Agent VM image ensure failed on node {node.Name} ({node.HostAddress}) for template {template.Name} ({template.Id}): {download.Message}");
        }

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

    public virtual async Task PullDockerImageAsync(Guid nodeId, string image, string? registryAuth, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new AgentClientException($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new { image, registryAuth });
        var response = await client.PostAsync("/api/images/pull-docker",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new AgentClientException(
                $"Agent Docker image pull failed on node {node.Name} ({node.HostAddress}) for image {image}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }
    }

    public virtual async Task DeleteDockerImageAsync(Guid nodeId, string image, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new AgentClientException($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/images/docker?image={Uri.EscapeDataString(image)}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new AgentClientException(
                $"Agent Docker image cleanup failed on node {node.Name} ({node.HostAddress}) for image {image}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }
    }

    public virtual async Task DeleteVmImageAsync(Guid nodeId, int templateId, string hash, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw new AgentClientException($"Fleet node {nodeId} was not found.");

        var path = $"/api/images/vm/{templateId}?hash={Uri.EscapeDataString(hash)}";
        var client = BuildClient(node);
        var response = await client.DeleteAsync(path, token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            throw new AgentClientException(
                $"Agent VM image cleanup failed on node {node.Name} ({node.HostAddress}) for template {templateId}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
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

    public virtual async Task<AgentVmImageDownloadResult> DownloadVmImageAsync(Guid nodeId, int templateId, string hash,
        string? downloadUrl = null, long? expectedSize = null, CancellationToken token = default)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return AgentVmImageDownloadResult.Failed($"Fleet node {nodeId} was not found.");

        VmArtifactDownload artifact;
        if (!string.IsNullOrWhiteSpace(downloadUrl))
        {
            artifact = new VmArtifactDownload(downloadUrl, hash, expectedSize ?? 0);
        }
        else
        {
            var serverUrl = NodeDeployService.ResolveServerUrl(_config).TrimEnd('/');
            artifact = new VmArtifactDownload(
                $"{serverUrl}/api/v1/image-templates/download/{hash}?nodeId={nodeId}",
                hash,
                0);
        }

        var client = BuildClient(node);
        client.Timeout = TimeSpan.FromHours(2);
        var registryReference = BuildVmRegistryReference(templateId, hash);

        var body = JsonSerializer.Serialize(new
        {
            templateId,
            hash = artifact.Sha256,
            expectedSize = artifact.Size > 0 ? artifact.Size : (long?)null,
            downloadUrl = artifact.DownloadUrl,
            authToken = node.AuthToken,
            registryAddress = registryReference.RegistryAddress,
            repository = registryReference.Repository,
            tag = registryReference.Tag,
            digest = registryReference.Digest
        });
        var response = await client.PostAsync("/api/images/download-vm",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(token);
            return AgentVmImageDownloadResult.Failed(
                $"Agent VM image download failed on node {node.Name} ({node.HostAddress}) for template {templateId}: {(int)response.StatusCode} {response.StatusCode}. {TrimResponseBody(responseBody)}");
        }

        var result = await response.Content.ReadFromJsonAsync<AgentVmImageDownloadResult>(token);
        return result ?? AgentVmImageDownloadResult.Failed(
            $"Agent returned an empty VM image download response on node {node.Name} ({node.HostAddress}).");
    }

    VmImageArtifactReference BuildVmRegistryReference(int templateId, string hash)
    {
        var settings = _config.GetSection(nameof(DockerRegistrySettings)).Get<DockerRegistrySettings>()
                       ?? new DockerRegistrySettings();
        var address = settings.NormalizedAddress;
        var ns = settings.NormalizedNamespace;
        var repository = string.IsNullOrWhiteSpace(ns)
            ? $"gzctf/vm-template/{templateId}"
            : $"{ns}/gzctf/vm-template/{templateId}";
        var digest = NormalizeSha256Digest(hash);
        return new VmImageArtifactReference(address, repository, digest, $"sha256:{digest}");
    }

    static string NormalizeSha256Digest(string hash)
    {
        var value = hash.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            value = value["sha256:".Length..];
        if (value.Length != 64)
            throw new AgentClientException("VM image sha256 digest is invalid.");
        return value.ToLowerInvariant();
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
    public bool ImageEnsured { get; set; }
    public string VmName { get; set; } = string.Empty;
    public int Memory { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    public string? Flag { get; set; }
    public List<AgentVmNetworkInterfaceRequest> Interfaces { get; set; } = [];
    public AgentVmInitConfig? CloudInit { get; set; }
}

public class AgentVmNetworkInterfaceRequest
{
    public string BridgeName { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public string Model { get; set; } = "e1000e";
    public string? InterfaceName { get; set; }
    public string? IpAddress { get; set; }
    public int? PrefixLength { get; set; }
    public string? Gateway { get; set; }
    public List<string> DnsServers { get; set; } = [];
    public List<string> Routes { get; set; } = [];
    public bool IsPrimary { get; set; }
}

public class AgentVmInitConfig
{
    public bool Enabled { get; set; }
    public OSType OsType { get; set; } = OSType.Linux;
    public string Hostname { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string UserData { get; set; } = string.Empty;
    public string MetaData { get; set; } = string.Empty;
    public string NetworkConfig { get; set; } = string.Empty;
    public List<string> SensitiveKeys { get; set; } = [];
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

public record AgentSyncRequest(
    string DownloadUrl,
    string? ExpectedSha256 = null,
    bool Restart = true);

public record AgentSyncResponse(
    bool Success,
    string Message,
    string? AgentVersion);

public record AgentVmImageDownloadResult(
    bool Success,
    string Message,
    bool AlreadyExists,
    bool Verified,
    long? Size,
    string? Digest)
{
    public static AgentVmImageDownloadResult Ok(bool alreadyExists, bool verified, long? size, string? digest) =>
        new(true, alreadyExists ? "Image already exists" : "Image ready", alreadyExists, verified, size, digest);

    public static AgentVmImageDownloadResult Failed(string message) =>
        new(false, message, false, false, null, null);
}

public record TeamLabStatusResponse(
    bool Available,
    bool Enable,
    bool DryRun,
    string AgentVersion,
    int ProtocolVersion,
    bool HasIpCommand,
    bool HasDockerCommand,
    bool HasKvmCommand,
    bool HasWireGuardCommand,
    bool HasIptablesCommand,
    bool HasNftCommand,
    bool HasTcpdumpCommand,
    bool HasDumpcapCommand,
    TeamLabToolCapabilityReport Capabilities,
    DateTimeOffset CheckedAt,
    string? Message = null);

public record TeamLabToolCapabilityReport(
    bool Docker,
    bool Kvm,
    bool KvmDevice,
    bool CpuVirtualization,
    bool WireGuard,
    bool Iptables,
    bool Nftables,
    bool Tcpdump,
    bool Dumpcap);

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
    string GatewayIp,
    string SourceIp = "");

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

public record TeamLabFabricApplyRequest(
    int RuntimeId,
    int RouteVersion,
    string FabricIp,
    string? NamespaceName = null,
    string NamespaceHostAddressCidr = "",
    string NamespacePeerAddressCidr = "",
    TeamLabStaticRouteRequest[]? LocalRoutes = null,
    TeamLabStaticRouteRequest[]? Routes = null,
    TeamLabForwardPolicyRequest[]? ForwardPolicies = null,
    bool DryRun = true);

public record TeamLabForwardPolicyRequest(
    string SourceCidr,
    string DestinationCidr,
    bool Allow);

public record TeamLabCaptureStartRequest(
    int RuntimeId,
    int JobId,
    string Scope,
    string InterfaceName,
    int MaxSeconds,
    long MaxBytes,
    bool DryRun = true);

public record TeamLabCaptureStopRequest(
    int RuntimeId,
    int JobId,
    bool DryRun = true);

public record TeamLabCaptureStatusRequest(
    int RuntimeId,
    int JobId,
    bool DryRun = true);

public record TeamLabCaptureResponse(
    bool Success,
    bool DryRun,
    string Message,
    string? FilePath,
    long CapturedBytes,
    bool Running,
    string[] Commands);

public record TeamLabFlowStartRequest(
    int RuntimeId,
    int? ShardId,
    int? NetworkId,
    string NetworkKey,
    string InterfaceName,
    bool DryRun = true);

public record TeamLabFlowStopRequest(
    int RuntimeId,
    string NetworkKey,
    bool DryRun = true);

public record TeamLabFlowSnapshotRequest(
    int RuntimeId,
    string NetworkKey,
    long AfterCursor = 0,
    bool DryRun = true);

public record TeamLabFlowSample(
    long Cursor,
    DateTimeOffset CapturedAt,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    long Bytes);

public record TeamLabFlowResponse(
    bool Success,
    bool DryRun,
    string Message,
    long NextCursor,
    TeamLabFlowSample[] Samples,
    string[] Commands);

public sealed record TeamLabCaptureDownloadResult(
    bool Success,
    string Message,
    Stream? Stream,
    string FileName,
    string ContentType,
    long? Length,
    IDisposable? Owner)
{
    public static TeamLabCaptureDownloadResult Failed(string message) =>
        new(false, message, null, string.Empty, MediaTypeNames.Application.Octet, null, null);

    public static TeamLabCaptureDownloadResult FromStream(Stream stream, string fileName, string contentType,
        long? length, IDisposable? owner) =>
        new(true, string.Empty, stream, fileName, contentType, length, owner);
}
