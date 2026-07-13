using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net.Mime;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Contracts;
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
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", node.AuthToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            AgentTelemetryHandler.WorkerNodeHeaderName, node.Id.ToString());
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
            throw NodeNotFound(nodeId, "container.create");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new
        {
            config.Generation, config.Image, config.TeamId, config.ChallengeId, config.UserId,
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
            using var deadline = CreateDeadline(token, TimeSpan.FromMinutes(3));
            response = await SendIdempotentAsync(client, () => new HttpRequestMessage(HttpMethod.Post,
                "/api/containers/create")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }, deadline.Token);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw TransportFailure(
                node.Id,
                "container.create",
                OperationalErrorCodes.AgentTimeout,
                $"Agent request timed out on node {node.Name} ({node.HostAddress}) while creating image {config.Image}.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw TransportFailure(
                node.Id,
                "container.create",
                OperationalErrorCodes.AgentConnectionFailed,
                $"Agent request failed on node {node.Name} ({node.HostAddress}) while creating image {config.Image}.",
                ex);
        }

        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "container.create",
                node.Id,
                $"Agent create container failed on node {node.Name} ({node.HostAddress}) for image {config.Image}.",
                token);

        var result = await response.Content.ReadFromJsonAsync<AgentCreateContainerResponse>(token);
        return result ?? throw InvalidAgentResponse(
            node.Id,
            "container.create",
            $"Agent returned an empty container response on node {node.Name} ({node.HostAddress}) for image {config.Image}.");
    }

    public virtual async Task DestroyContainerAsync(Guid nodeId, string containerId, CancellationToken token)
        => await DestroyContainerAsync(nodeId, containerId, null, token);

    public virtual async Task DestroyContainerAsync(
        Guid nodeId,
        string containerId,
        int? expectedGeneration,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw NodeNotFound(nodeId, "container.destroy");

        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(60));
        var generationQuery = expectedGeneration is { } generation ? $"?generation={generation}" : string.Empty;
        var response = await client.DeleteAsync(
            $"/api/containers/{Uri.EscapeDataString(containerId)}{generationQuery}",
            deadline.Token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw await CreateAgentExceptionAsync(
                response,
                "container.destroy",
                node.Id,
                $"Agent container deletion failed on node {node.Name} ({node.HostAddress}).",
                token);
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
            var error = await ReadAgentErrorAsync(
                response, "container.exec", node.Id, "Agent container command failed.", token);
            return AgentCommandResult.Failed(null, error.Message);
        }

        var result = await response.Content.ReadFromJsonAsync<AgentCommandResult>(token);
        return result ?? AgentCommandResult.Failed(null, "Agent returned an empty command result.");
    }

    public async Task RemoveNetworkAsync(Guid nodeId, string networkName, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw NodeNotFound(nodeId, "container.network.delete");

        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/containers/networks/{Uri.EscapeDataString(networkName)}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw await CreateAgentExceptionAsync(
                response,
                "container.network.delete",
                node.Id,
                $"Agent network deletion failed on node {node.Name} ({node.HostAddress}), network {networkName}.",
                token);
        }
    }

    public virtual async Task<AgentRuntimeInventoryResponse> GetRuntimeInventoryAsync(
        Guid nodeId,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw NodeNotFound(nodeId, "runtime.inventory");

        var client = BuildClient(node);
        HttpResponseMessage response;
        try
        {
            using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(30));
            response = await client.GetAsync("/api/runtime/inventory", deadline.Token);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw TransportFailure(
                node.Id,
                "runtime.inventory",
                OperationalErrorCodes.AgentTimeout,
                $"Agent runtime inventory timed out on node {node.Name} ({node.HostAddress}).",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw TransportFailure(
                node.Id,
                "runtime.inventory",
                OperationalErrorCodes.AgentConnectionFailed,
                $"Agent runtime inventory failed on node {node.Name} ({node.HostAddress}).",
                ex);
        }

        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "runtime.inventory",
                node.Id,
                $"Agent runtime inventory failed on node {node.Name} ({node.HostAddress}).",
                token);

        var result = await response.Content.ReadFromJsonAsync<AgentRuntimeInventoryResponse>(token);
        return result ?? throw InvalidAgentResponse(
            node.Id,
            "runtime.inventory",
            $"Agent returned an empty runtime inventory on node {node.Name} ({node.HostAddress}).");
    }

    public async Task<TeamLabStatusResponse?> GetTeamLabStatusAsync(Guid nodeId, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return null;

        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(5));
        var response = await client.GetAsync("/api/teamlab/status", deadline.Token);
        return await ReadTeamLabResponseAsync<TeamLabStatusResponse>(
            response, "teamlab.status", node.Id, deadline.Token);
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
            throw TransportFailure(
                node.Id,
                "maintenance.sync",
                OperationalErrorCodes.AgentTimeout,
                $"Agent sync request timed out on node {node.Name} ({node.HostAddress}).",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw TransportFailure(
                node.Id,
                "maintenance.sync",
                OperationalErrorCodes.AgentConnectionFailed,
                $"Agent sync request failed on node {node.Name} ({node.HostAddress}).",
                ex);
        }

        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "maintenance.sync",
                node.Id,
                $"Agent sync failed on node {node.Name} ({node.HostAddress}).",
                token);

        var result = await response.Content.ReadFromJsonAsync<AgentSyncResponse>(token)
                     ?? new AgentSyncResponse(false, "Agent returned an empty sync response.", null);
        if (!result.Success || string.IsNullOrWhiteSpace(request.ExpectedSha256))
            return result;
        var manifest = await WaitForCapabilityManifestAsync(client, request.ExpectedSha256, token);
        return manifest is null
            ? new AgentSyncResponse(false,
                "Agent binary synchronized, but the updated capability manifest was not observed.", result.AgentVersion)
            : result with { Message = "Agent synchronized and capability manifest confirmed." };
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
            var error = await ReadAgentErrorAsync(
                response,
                "teamlab.capture.download",
                node.Id,
                "Agent TeamLab capture download failed.",
                token);
            return TeamLabCaptureDownloadResult.Failed(error.Message);
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
        using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(60));
        var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"),
            deadline.Token);
        return await ReadTeamLabResponseAsync<TResponse>(
            response,
            AgentOperationName.Resolve(HttpMethod.Post, path),
            node.Id,
            deadline.Token);
    }

    private async Task<T?> ReadTeamLabResponseAsync<T>(
        HttpResponseMessage response,
        string operation,
        Guid nodeId,
        CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadAgentErrorAsync(
                response, operation, nodeId, "Agent TeamLab request failed.", token);
            _logger.LogWarning(
                "Agent TeamLab request failed with {ErrorCategory}/{ErrorCode} on node {NodeId}.",
                error.Category, error.Code, nodeId);
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
        return await PostFabricAsync(client, node.Id, "/api/containers/fabric/networks",
            new { networkName, cidr }, token);
    }

    public async Task<PenetrationFabricResult> AttachFabricInterfaceAsync(Guid nodeId, string containerId,
        PenetrationFabricInterfaceSpec spec, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return PenetrationFabricResult.Unsupported($"Fleet node {nodeId} was not found.");

        var client = BuildClient(node);
        return await PostFabricAsync(client, node.Id,
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
        return await PostFabricAsync(client, node.Id,
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
        return await PostFabricAsync(client, node.Id,
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
        return await PostFabricAsync(client, node.Id,
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
            var error = await ReadAgentErrorAsync(
                response,
                "fabric.network.delete",
                node.Id,
                "Agent fabric network deletion failed.",
                token);
            return PenetrationFabricResult.Failed(null, error.Message);
        }

        var result = await response.Content.ReadFromJsonAsync<PenetrationFabricResult>(token);
        return result ?? PenetrationFabricResult.Failed(null, "Agent returned an empty fabric result.");
    }

    static async Task<PenetrationFabricResult> PostFabricAsync(
        HttpClient client,
        Guid nodeId,
        string path,
        object body,
        CancellationToken token)
    {
        var response = await client.PostAsync(path,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadAgentErrorAsync(
                response,
                AgentOperationName.Resolve(HttpMethod.Post, path),
                nodeId,
                "Agent fabric command failed.",
                token);
            return PenetrationFabricResult.Failed(null, error.Message);
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
                throw new AgentClientException(new OperationalError(
                    OperationalErrorCategory.ImageRegistry,
                    OperationalErrorCodes.ImageArtifactMissing,
                    $"VM template {request.TemplateId.Value} is missing or has no image hash.",
                    false,
                    WorkerNodeId: node.Id,
                    Operation: "image.vm.ensure"));

            var download = await DownloadVmImageAsync(nodeId, template.Id, template.ImageHash, token: token);
            if (!download.Success)
                throw new AgentClientException(new OperationalError(
                    OperationalErrorCategory.ImageTransfer,
                    OperationalErrorCodes.ImageTransferFailed,
                    $"Agent VM image ensure failed on node {node.Name} ({node.HostAddress}) for template {template.Name} ({template.Id}): {download.Message}",
                    true,
                    WorkerNodeId: node.Id,
                    Operation: "image.vm.ensure"));
        }

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(request);
        using var deadline = CreateDeadline(token, TimeSpan.FromMinutes(5));
        var response = await SendIdempotentAsync(client, () => new HttpRequestMessage(HttpMethod.Post,
            "/api/vms/create")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }, deadline.Token);

        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.create",
                node.Id,
                $"Agent VM creation failed on node {node.Name} ({node.HostAddress}).",
                token);

        return await response.Content.ReadFromJsonAsync<AgentCreateVmResponse>(token)
               ?? throw InvalidAgentResponse(
                   node.Id,
                   "vm.create",
                   $"Agent returned an empty VM response on node {node.Name} ({node.HostAddress}).");
    }

    public virtual async Task DestroyVmAsync(Guid nodeId, string vmName, CancellationToken token)
        => await DestroyVmAsync(nodeId, vmName, null, null, token);

    public virtual async Task DestroyVmAsync(
        Guid nodeId,
        string vmName,
        int? expectedGeneration,
        string? expectedNativeId,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw NodeNotFound(nodeId, "vm.destroy");

        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(60));
        var query = new List<string>(2);
        if (expectedGeneration is { } generation)
            query.Add($"generation={generation}");
        if (!string.IsNullOrWhiteSpace(expectedNativeId))
            query.Add($"nativeId={Uri.EscapeDataString(expectedNativeId)}");
        var suffix = query.Count == 0 ? string.Empty : $"?{string.Join('&', query)}";
        var response = await client.DeleteAsync(
            $"/api/vms/{Uri.EscapeDataString(vmName)}{suffix}", deadline.Token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw await CreateAgentExceptionAsync(
                response,
                "vm.destroy",
                node.Id,
                $"Agent VM deletion failed on node {node.Name} ({node.HostAddress}), VM {vmName}.",
                token);
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
            throw NodeNotFound(nodeId, "image.docker.pull");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new { image, registryAuth });
        using var deadline = CreateDeadline(token, TimeSpan.FromHours(2));
        var response = await client.PostAsync("/api/images/pull-docker",
            new StringContent(body, Encoding.UTF8, "application/json"), deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateAgentExceptionAsync(
                response,
                "image.docker.pull",
                node.Id,
                $"Agent Docker image pull failed on node {node.Name} ({node.HostAddress}) for image {image}.",
                token);
        }
    }

    public virtual async Task DeleteDockerImageAsync(Guid nodeId, string image, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw NodeNotFound(nodeId, "image.docker.delete");

        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/images/docker?image={Uri.EscapeDataString(image)}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw await CreateAgentExceptionAsync(
                response,
                "image.docker.delete",
                node.Id,
                $"Agent Docker image cleanup failed on node {node.Name} ({node.HostAddress}) for image {image}.",
                token);
        }
    }

    public virtual async Task DeleteVmImageAsync(Guid nodeId, int templateId, string hash, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw NodeNotFound(nodeId, "image.vm.delete");

        var path = $"/api/images/vm/{templateId}?hash={Uri.EscapeDataString(hash)}";
        var client = BuildClient(node);
        var response = await client.DeleteAsync(path, token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw await CreateAgentExceptionAsync(
                response,
                "image.vm.delete",
                node.Id,
                $"Agent VM image cleanup failed on node {node.Name} ({node.HostAddress}) for template {templateId}.",
                token);
        }
    }

    public async Task EnsureDockerRegistryAsync(Guid nodeId, int port, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            throw NodeNotFound(nodeId, "image.registry.ensure");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new { port = Math.Clamp(port, 1, 65535) });
        var response = await client.PostAsync("/api/images/ensure-docker-registry",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateAgentExceptionAsync(
                response,
                "image.registry.ensure",
                node.Id,
                $"Agent Docker registry bootstrap failed on node {node.Name} ({node.HostAddress}).",
                token);
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
            throw NodeNotFound(nodeId, "image.registry.configure");

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new { registries });
        var response = await client.PostAsync("/api/images/configure-docker-registry",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateAgentExceptionAsync(
                response,
                "image.registry.configure",
                node.Id,
                $"Agent Docker registry trust configuration failed on node {node.Name} ({node.HostAddress}).",
                token);
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
        using var deadline = CreateDeadline(token, TimeSpan.FromHours(2));
        var response = await client.PostAsync("/api/images/download-vm",
            new StringContent(body, Encoding.UTF8, "application/json"), deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadAgentErrorAsync(
                response,
                "image.vm.download",
                node.Id,
                $"Agent VM image download failed on node {node.Name} ({node.HostAddress}) for template {templateId}.",
                token);
            return AgentVmImageDownloadResult.Failed(error.Message);
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
            throw new AgentClientException(new OperationalError(
                OperationalErrorCategory.Validation,
                OperationalErrorCodes.RequestInvalid,
                "VM image sha256 digest is invalid.",
                false,
                Operation: "image.vm.reference"));
        return value.ToLowerInvariant();
    }

    private static AgentClientException NodeNotFound(Guid nodeId, string operation) =>
        new(new OperationalError(
            OperationalErrorCategory.NodeUnavailable,
            OperationalErrorCodes.NodeNotFound,
            $"Fleet node {nodeId} was not found.",
            false,
            WorkerNodeId: nodeId,
            Operation: operation));

    private static AgentClientException InvalidAgentResponse(
        Guid nodeId,
        string operation,
        string message) =>
        new(new OperationalError(
            OperationalErrorCategory.AgentProtocol,
            OperationalErrorCodes.AgentResponseInvalid,
            message,
            false,
            WorkerNodeId: nodeId,
            Operation: operation));

    private static AgentClientException TransportFailure(
        Guid nodeId,
        string operation,
        string code,
        string message,
        Exception innerException) =>
        new(new OperationalError(
            OperationalErrorCategory.AgentTransport,
            code,
            message,
            true,
            WorkerNodeId: nodeId,
            Operation: operation), innerException);

    private static async Task<AgentClientException> CreateAgentExceptionAsync(
        HttpResponseMessage response,
        string operation,
        Guid nodeId,
        string fallbackMessage,
        CancellationToken token) =>
        new(await ReadAgentErrorAsync(response, operation, nodeId, fallbackMessage, token));

    private static async Task<OperationalError> ReadAgentErrorAsync(
        HttpResponseMessage response,
        string operation,
        Guid nodeId,
        string fallbackMessage,
        CancellationToken token)
    {
        AgentErrorResponse? payload = null;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<AgentErrorResponse>(token);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Older or malformed Agent responses fail closed to HTTP status classification.
        }

        return OperationalErrorClassifier.FromAgentResponse(
            payload,
            (int)response.StatusCode,
            operation,
            fallbackMessage,
            nodeId);
    }

    async Task<AgentCapabilityManifest?> WaitForCapabilityManifestAsync(HttpClient client, string expectedSha256,
        CancellationToken token)
    {
        var expected = expectedSha256.Trim().Replace("sha256:", string.Empty,
            StringComparison.OrdinalIgnoreCase);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            try
            {
                using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(5));
                using var response = await client.GetAsync("/api/status", deadline.Token);
                if (response.IsSuccessStatusCode)
                {
                    var manifest = await response.Content.ReadFromJsonAsync<AgentCapabilityManifest>(deadline.Token);
                    if (manifest?.ManifestSchemaVersion == AgentCapabilityEvaluator.SupportedManifestSchema &&
                        string.Equals(manifest.BinarySha256, expected, StringComparison.OrdinalIgnoreCase))
                        return manifest;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException &&
                                       !token.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Waiting for Agent capability manifest after sync.");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        return null;
    }

    static CancellationTokenSource CreateDeadline(CancellationToken token, TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        source.CancelAfter(timeout);
        return source;
    }

    static async Task<HttpResponseMessage> SendIdempotentAsync(HttpClient client,
        Func<HttpRequestMessage> requestFactory, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = requestFactory();
                return await client.SendAsync(request, token);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150), token);
            }
        }
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
    public OperationalError Error { get; }

    public AgentClientException(OperationalError error) : base(error.Message)
    {
        Error = error;
    }

    public AgentClientException(OperationalError error, Exception innerException) : base(error.Message, innerException)
    {
        Error = error;
    }
}

public class AgentCreateVmRequest
{
    public int Generation { get; set; } = 1;
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
    public string NativeId { get; set; } = string.Empty;
    public int Generation { get; set; } = 1;
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
