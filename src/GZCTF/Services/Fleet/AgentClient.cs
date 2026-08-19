using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.GuestControl.Contracts;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts.Execution;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Container.Manager;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class AgentClient
{
    private static readonly TimeSpan TeamLabRequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EndpointSensorStartTimeout = TimeSpan.FromMinutes(2);

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
            config.RuntimeId, config.Generation, config.Image, config.TeamId, config.ChallengeId, config.UserId,
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

    public async Task ProxyRemoteTerminalAsync(Guid nodeId, Guid sessionId, int runtimeId, int generation,
        string containerId, DateTimeOffset expiresAt, WebSocket browser, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token) ?? throw NodeNotFound(nodeId, "remote.terminal");
        using var agent = new ClientWebSocket();
        agent.Options.SetRequestHeader("Authorization", $"Bearer {node.AuthToken}");
        var uri = new Uri($"ws://{node.HostAddress}:{node.AgentPort}/api/remote-access/terminals/{sessionId:D}?runtimeId={runtimeId}&generation={generation}&containerId={Uri.EscapeDataString(containerId)}&expiresAt={Uri.EscapeDataString(expiresAt.ToString("O"))}");
        await agent.ConnectAsync(uri, token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        var forward = CopyWebSocketAsync(browser, agent, linked.Token);
        var reverse = CopyWebSocketAsync(agent, browser, linked.Token);
        await Task.WhenAny(forward, reverse);
        linked.Cancel();
        try { await Task.WhenAll(forward, reverse); } catch (OperationCanceledException) { }
        if (browser.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await browser.CloseAsync(WebSocketCloseStatus.NormalClosure, "terminal_closed", CancellationToken.None);
    }

    private static async Task CopyWebSocketAsync(WebSocket source, WebSocket target, CancellationToken token)
    {
        var buffer = new byte[8192];
        while (source.State == WebSocketState.Open && target.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await source.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return;
            await target.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, token);
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

    public virtual Task<TeamLabExecutionPlanApplyResponse?> ApplyTeamLabExecutionPlanAsync(
        Guid nodeId,
        TeamLabExecutionPlanV2 plan,
        CancellationToken token,
        TimeSpan? requestTimeout = null) =>
        PostTeamLabAsync<TeamLabExecutionPlanApplyRequest, TeamLabExecutionPlanApplyResponse>(
            nodeId,
            "/api/teamlab/execution-plan/apply",
            new TeamLabExecutionPlanApplyRequest(plan),
            token,
            requestTimeout);

    public virtual Task<TeamLabExecutionPlanCleanupResponse?> CleanupTeamLabExecutionPlanAsync(
        Guid nodeId,
        TeamLabExecutionPlanV2 plan,
        CancellationToken token,
        TimeSpan? requestTimeout = null) =>
        PostTeamLabAsync<TeamLabExecutionPlanCleanupRequest, TeamLabExecutionPlanCleanupResponse>(
            nodeId,
            "/api/teamlab/execution-plan/cleanup",
            new TeamLabExecutionPlanCleanupRequest(plan),
            token,
            requestTimeout);

    public virtual async Task<TeamLabDryRunResponse?> ConfigureTeamLabWireGuardAsync(Guid nodeId,
        TeamLabWireGuardRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabWireGuardRequest, TeamLabDryRunResponse>(nodeId,
            "/api/teamlab/wireguard", request, token);

    public virtual async Task<TeamLabDryRunResponse?> CleanupTeamLabWireGuardAsync(Guid nodeId,
        TeamLabWireGuardCleanupRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabWireGuardCleanupRequest, TeamLabDryRunResponse>(nodeId,
            "/api/teamlab/wireguard/cleanup", request, token);

    public virtual async Task<TeamLabDryRunResponse?> CleanupTeamLabAsync(Guid nodeId, TeamLabCleanupRequest request,
        CancellationToken token) => await PostTeamLabAsync<TeamLabCleanupRequest, TeamLabDryRunResponse>(nodeId,
        "/api/teamlab/cleanup", request, token);

    public virtual async Task<TeamLabAssetLifecycleResponse?> PauseTeamLabAssetAsync(
        Guid nodeId,
        TeamLabAssetLifecycleRequest request,
        CancellationToken token) =>
        await PostTeamLabAsync<TeamLabAssetLifecycleRequest, TeamLabAssetLifecycleResponse>(
            nodeId, "/api/teamlab/assets/pause", request, token);

    public virtual async Task<TeamLabAssetLifecycleResponse?> ResumeTeamLabAssetAsync(
        Guid nodeId,
        TeamLabAssetLifecycleRequest request,
        CancellationToken token) =>
        await PostTeamLabAsync<TeamLabAssetLifecycleRequest, TeamLabAssetLifecycleResponse>(
            nodeId, "/api/teamlab/assets/resume", request, token);

    public virtual async Task<TeamLabDryRunResponse?> ProbeTeamLabAsync(Guid nodeId, TeamLabProbeRequest request,
        CancellationToken token) => await PostTeamLabAsync<TeamLabProbeRequest, TeamLabDryRunResponse>(nodeId,
        "/api/teamlab/probe", request, token);

    public virtual async Task<TeamLabDryRunResponse?> AttachTeamLabContainerAsync(Guid nodeId,
        TeamLabContainerAttachRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabContainerAttachRequest, TeamLabDryRunResponse>(nodeId,
            "/api/teamlab/containers/attach", request, token);

    public virtual async Task<TeamLabContainerNetworkFinalizeResponse?> FinalizeTeamLabContainerNetworkAsync(
        Guid nodeId,
        TeamLabContainerNetworkFinalizeRequest request,
        CancellationToken token) =>
        await PostTeamLabAsync<TeamLabContainerNetworkFinalizeRequest, TeamLabContainerNetworkFinalizeResponse>(
            nodeId, "/api/teamlab/containers/network/finalize", request, token);

    public virtual async Task<TeamLabInfrastructureApplyResponse?> ApplyTeamLabInfrastructureAsync(
        Guid nodeId,
        TeamLabInfrastructureApplyRequest request,
        CancellationToken token) =>
        await PostTeamLabAsync<TeamLabInfrastructureApplyRequest, TeamLabInfrastructureApplyResponse>(
            nodeId, "/api/teamlab/shards/apply", request, token);

    public virtual async Task<TeamLabInfrastructureStateResponse> GetTeamLabInfrastructureStateAsync(
        Guid nodeId,
        int runtimeId,
        int generation,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "teamlab.infrastructure.state");
        var client = BuildClient(node);
        using var response = await client.GetAsync(
            $"/api/teamlab/runtime/{runtimeId}/generation/{generation}/state", token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "teamlab.infrastructure.state",
                node.Id,
                $"Agent TeamLab infrastructure state failed on node {node.Name} ({node.HostAddress}).",
                token);
        return await response.Content.ReadFromJsonAsync<TeamLabInfrastructureStateResponse>(token)
               ?? throw InvalidAgentResponse(
                   node.Id,
                   "teamlab.infrastructure.state",
                   $"Agent returned an empty TeamLab infrastructure state on node {node.Name} ({node.HostAddress}).");
    }

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

    public virtual async Task<TeamLabCaptureResponse?> UploadTeamLabCaptureAsync(Guid nodeId,
        TeamLabCaptureUploadRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabCaptureUploadRequest, TeamLabCaptureResponse>(nodeId,
            "/api/teamlab/capture/upload", request, token);

    public virtual async Task<TeamLabCaptureResponse?> DeleteTeamLabCaptureAsync(Guid nodeId,
        TeamLabCaptureDeleteRequest request, CancellationToken token) =>
        await PostTeamLabAsync<TeamLabCaptureDeleteRequest, TeamLabCaptureResponse>(nodeId,
            "/api/teamlab/capture/delete", request, token);

    public virtual async Task<TeamLabLinkPolicyResponse?> ApplyTeamLabLinkPolicyAsync(
        Guid nodeId,
        TeamLabLinkPolicyApplyRequest request,
        CancellationToken token,
        TimeSpan? requestTimeout = null) =>
        await PostTeamLabAsync<TeamLabLinkPolicyApplyRequest, TeamLabLinkPolicyResponse>(
            nodeId, "/api/teamlab/link-policy/apply", request, token, requestTimeout);

    public virtual async Task<TeamLabLinkPolicyResponse?> RecoverTeamLabLinkPolicyAsync(
        Guid nodeId,
        TeamLabLinkPolicyRecoverRequest request,
        CancellationToken token,
        TimeSpan? requestTimeout = null) =>
        await PostTeamLabAsync<TeamLabLinkPolicyRecoverRequest, TeamLabLinkPolicyResponse>(
            nodeId, "/api/teamlab/link-policy/recover", request, token, requestTimeout);

    public virtual async Task<TeamLabObservationBatchResponse?> ReadTeamLabObservationsAsync(
        Guid nodeId,
        TeamLabObservationBatchRequest request,
        CancellationToken token) =>
        await PostTeamLabAsync<TeamLabObservationBatchRequest, TeamLabObservationBatchResponse>(
            nodeId, "/api/teamlab/observations/read", request, token);

    public virtual async Task<TeamLabEndpointSensorResponse?> RegisterTeamLabEndpointSensorAsync(
        Guid nodeId,
        TeamLabEndpointSensorRegistrationRequest request,
        CancellationToken token) =>
        await PostTeamLabAsync<TeamLabEndpointSensorRegistrationRequest, TeamLabEndpointSensorResponse>(
            nodeId, "/api/teamlab/sensors/register", request, token);

    public virtual async Task<TeamLabEndpointSensorResponse?> RemoveTeamLabEndpointSensorAsync(
        Guid nodeId,
        TeamLabEndpointSensorRemoveRequest request,
        CancellationToken token) =>
        await PostTeamLabAsync<TeamLabEndpointSensorRemoveRequest, TeamLabEndpointSensorResponse>(
            nodeId, "/api/teamlab/sensors/remove", request, token);

    public virtual async Task<TeamLabEndpointSensorResponse?> StartTeamLabEndpointSensorAsync(
        Guid nodeId,
        TeamLabEndpointSensorStartRequest request,
        CancellationToken token) =>
        await PostTeamLabAsync<TeamLabEndpointSensorStartRequest, TeamLabEndpointSensorResponse>(
            nodeId, "/api/teamlab/sensors/start", request, token, EndpointSensorStartTimeout);

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

    private async Task<TResponse?> PostTeamLabAsync<TRequest, TResponse>(
        Guid nodeId,
        string path,
        TRequest request,
        CancellationToken token,
        TimeSpan? requestTimeout = null)
    {
        var operation = AgentOperationName.Resolve(HttpMethod.Post, path);
        var node = await GetNodeAsync(nodeId, token)
            ?? throw NodeNotFound(nodeId, operation);

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(request);
        using var deadline = CreateDeadline(token, requestTimeout ?? TeamLabRequestTimeout);
        var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"),
            deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateAgentExceptionAsync(
                response,
                operation,
                node.Id,
                "Agent TeamLab request failed.",
                deadline.Token);
        }

        return await response.Content.ReadFromJsonAsync<TResponse>(deadline.Token);
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

    public virtual async Task<AgentCommitVmScenarioResponse> CommitVmScenarioAsync(
        Guid nodeId,
        AgentCommitVmScenarioRequest request,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "image.vm.scenario-commit");
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromMinutes(45));
        using var response = await client.PostAsync(
            "/api/vms/scenario-artifacts",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "image.vm.scenario-commit",
                node.Id,
                $"Scenario artifact capture failed on node {node.Name} ({node.HostAddress}).",
                token);
        return await response.Content.ReadFromJsonAsync<AgentCommitVmScenarioResponse>(token)
               ?? throw InvalidAgentResponse(
                   node.Id,
                   "image.vm.scenario-commit",
                   $"Agent returned an empty scenario artifact response on node {node.Name} ({node.HostAddress}).");
    }

    public virtual async Task<GuestControlPrepareResponse> PrepareGuestControlAsync(
        Guid nodeId,
        GuestControlPrepareRequest request,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.guest-control.prepare");
        var client = BuildClient(node);
        using var response = await client.PostAsync(
            "/api/guest-control/prepare",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.guest-control.prepare",
                node.Id,
                $"Guest control preparation failed on node {node.Name} ({node.HostAddress}).",
                token);
        return await response.Content.ReadFromJsonAsync<GuestControlPrepareResponse>(token)
               ?? throw InvalidAgentResponse(node.Id, "vm.guest-control.prepare",
                   "Agent returned an empty guest-control preparation response.");
    }

    public virtual async Task<GuestManagementEndpointInfo> GetGuestManagementEndpointAsync(
        Guid nodeId,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.guest-control.network");
        var client = BuildClient(node);
        using var response = await client.GetAsync("/api/guest-control/network", token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.guest-control.network",
                node.Id,
                $"Guest management endpoint query failed on node {node.Name} ({node.HostAddress}).",
                token);
        return await response.Content.ReadFromJsonAsync<GuestManagementEndpointInfo>(token)
               ?? throw InvalidAgentResponse(node.Id, "vm.guest-control.network",
                   "Agent returned an empty guest-management endpoint response.");
    }

    public virtual async Task<GuestControlStatus?> GetGuestControlStatusAsync(
        Guid nodeId,
        GuestAssetIdentity identity,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.guest-control.status");
        var client = BuildClient(node);
        using var response = await client.PostAsync(
            "/api/guest-control/status",
            new StringContent(JsonSerializer.Serialize(identity), Encoding.UTF8, "application/json"), token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.guest-control.status",
                node.Id,
                $"Guest control status query failed on node {node.Name} ({node.HostAddress}).",
                token);
        return await response.Content.ReadFromJsonAsync<GuestControlStatus>(token);
    }

    public virtual async Task StageGuestConformancePackageAsync(
        Guid nodeId,
        AgentGuestConformancePackageRequest request,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.guest-control.conformance-package");
        var client = BuildClient(node);
        using var response = await client.PostAsync(
            "/api/guest-control/conformance-package",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.guest-control.conformance-package",
                node.Id,
                $"Guest conformance package staging failed on node {node.Name} ({node.HostAddress}).",
                token);
    }

    public virtual async Task<bool> WaitVmCleanShutdownAsync(
        Guid nodeId,
        string vmName,
        int timeoutSeconds,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.wait-clean-shutdown");
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds + 15, 30, 1815)));
        using var response = await client.PostAsync(
            $"/api/vms/{Uri.EscapeDataString(vmName)}/wait-clean-shutdown?timeoutSeconds={Math.Clamp(timeoutSeconds, 1, 1800)}",
            null,
            deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.wait-clean-shutdown",
                node.Id,
                $"VM clean-shutdown wait failed on node {node.Name} ({node.HostAddress}).",
                token);
        var result = await response.Content.ReadFromJsonAsync<AgentVmCleanShutdownResponse>(token);
        return result?.CleanShutdown == true;
    }

    public virtual async Task<AgentVmBootstrapApplyResponse> ApplyVmBootstrapAsync(
        Guid nodeId,
        string vmName,
        AgentVmBootstrapApplyRequest request,
        CancellationToken token) =>
        await ApplyVmBootstrapAsync(nodeId, vmName, request, null, token);

    public virtual async Task<AgentVmBootstrapApplyResponse> ApplyVmBootstrapAsync(
        Guid nodeId,
        string vmName,
        AgentVmBootstrapApplyRequest request,
        string? expectedNativeId,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.bootstrap.apply");
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromMinutes(45));
        using var response = await client.PostAsync(
            $"/api/vms/{Uri.EscapeDataString(vmName)}/bootstrap/apply{BuildVmIdentityQuery(null, expectedNativeId)}",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.bootstrap.apply",
                node.Id,
                $"Agent VM bootstrap failed on node {node.Name} ({node.HostAddress}), VM {vmName}.",
                token);
        return await response.Content.ReadFromJsonAsync<AgentVmBootstrapApplyResponse>(token)
               ?? throw InvalidAgentResponse(
                   node.Id,
                   "vm.bootstrap.apply",
                   $"Agent returned an empty VM bootstrap response on node {node.Name} ({node.HostAddress}).");
    }

    public virtual async Task<AgentVmGuestStatusResponse> WaitVmGuestAsync(
        Guid nodeId,
        string vmName,
        int timeoutSeconds,
        CancellationToken token) =>
        await WaitVmGuestAsync(nodeId, vmName, timeoutSeconds, null, null, token);

    public virtual async Task<AgentVmGuestStatusResponse> WaitVmGuestAsync(
        Guid nodeId,
        string vmName,
        int timeoutSeconds,
        int? expectedGeneration,
        string? expectedNativeId,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.guest.wait");
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds + 30, 40, 630)));
        using var response = await client.PostAsync(
            $"/api/vms/{Uri.EscapeDataString(vmName)}/guest/wait{BuildVmIdentityQuery(expectedGeneration, expectedNativeId)}",
            new StringContent(JsonSerializer.Serialize(new { timeoutSeconds }), Encoding.UTF8, "application/json"),
            deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.guest.wait",
                node.Id,
                $"Agent VM guest wait failed on node {node.Name} ({node.HostAddress}), VM {vmName}.",
                token);
        return await response.Content.ReadFromJsonAsync<AgentVmGuestStatusResponse>(token)
               ?? throw InvalidAgentResponse(
                   node.Id,
                   "vm.guest.wait",
                   $"Agent returned an empty guest status on node {node.Name} ({node.HostAddress}).");
    }

    public virtual async Task<AgentVmBootstrapApplyResponse> CheckVmBootstrapHealthAsync(
        Guid nodeId,
        string vmName,
        AgentVmBootstrapApplyRequest request,
        CancellationToken token) =>
        await CheckVmBootstrapHealthAsync(nodeId, vmName, request, null, token);

    public virtual async Task<AgentVmBootstrapApplyResponse> CheckVmBootstrapHealthAsync(
        Guid nodeId,
        string vmName,
        AgentVmBootstrapApplyRequest request,
        string? expectedNativeId,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.bootstrap.health");
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromMinutes(30));
        using var response = await client.PostAsync(
            $"/api/vms/{Uri.EscapeDataString(vmName)}/bootstrap/health{BuildVmIdentityQuery(null, expectedNativeId)}",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.bootstrap.health",
                node.Id,
                $"Agent VM health probe failed on node {node.Name} ({node.HostAddress}), VM {vmName}.",
                token);
        return await response.Content.ReadFromJsonAsync<AgentVmBootstrapApplyResponse>(token)
               ?? throw InvalidAgentResponse(
                   node.Id,
                   "vm.bootstrap.health",
                   $"Agent returned an empty VM health response on node {node.Name} ({node.HostAddress}).");
    }

    public virtual async Task<AgentVmCapabilityProbeResponse> ProbeVmCapabilitiesAsync(
        Guid nodeId,
        string vmName,
        AgentVmCapabilityProbeRequest request,
        CancellationToken token) =>
        await ProbeVmCapabilitiesAsync(nodeId, vmName, request, null, null, token);

    public virtual async Task<AgentVmCapabilityProbeResponse> ProbeVmCapabilitiesAsync(
        Guid nodeId,
        string vmName,
        AgentVmCapabilityProbeRequest request,
        int? expectedGeneration,
        string? expectedNativeId,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token)
                   ?? throw NodeNotFound(nodeId, "vm.capabilities.probe");
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromMinutes(15));
        using var response = await client.PostAsync(
            $"/api/vms/{Uri.EscapeDataString(vmName)}/capabilities/probe{BuildVmIdentityQuery(expectedGeneration, expectedNativeId)}",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "vm.capabilities.probe",
                node.Id,
                $"Agent VM capability probe failed on node {node.Name} ({node.HostAddress}), VM {vmName}.",
                token);
        return await response.Content.ReadFromJsonAsync<AgentVmCapabilityProbeResponse>(token)
               ?? throw InvalidAgentResponse(
                   node.Id,
                   "vm.capabilities.probe",
                   $"Agent returned an empty VM capability response on node {node.Name} ({node.HostAddress}).");
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
        await GetVmIpAsync(nodeId, vmName, [], null, null, token);

    public async Task<AgentVmIpResponse?> GetVmIpAsync(Guid nodeId, string vmName,
        IReadOnlyList<AgentVmNetworkInterfaceRequest> interfaces, CancellationToken token) =>
        await GetVmIpAsync(nodeId, vmName, interfaces, null, null, token);

    public async Task<AgentVmIpResponse?> GetVmIpAsync(
        Guid nodeId,
        string vmName,
        IReadOnlyList<AgentVmNetworkInterfaceRequest> interfaces,
        int? expectedGeneration,
        string? expectedNativeId,
        CancellationToken token) =>
        await GetVmIpAsync(
            nodeId, vmName, interfaces, expectedGeneration, expectedNativeId, 3389, token);

    public async Task<AgentVmIpResponse?> GetVmIpAsync(
        Guid nodeId,
        string vmName,
        int rdpTargetPort,
        int? expectedGeneration,
        string? expectedNativeId,
        CancellationToken token) =>
        await GetVmIpAsync(
            nodeId, vmName, [], expectedGeneration, expectedNativeId, rdpTargetPort, token);

    private async Task<AgentVmIpResponse?> GetVmIpAsync(
        Guid nodeId,
        string vmName,
        IReadOnlyList<AgentVmNetworkInterfaceRequest> interfaces,
        int? expectedGeneration,
        string? expectedNativeId,
        int rdpTargetPort,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return null;

        var client = BuildClient(node);
        try
        {
            using var deadline = CreateDeadline(token, TimeSpan.FromSeconds(5));
            var identityQuery = BuildVmIdentityQuery(expectedGeneration, expectedNativeId);
            var separator = string.IsNullOrEmpty(identityQuery) ? '?' : '&';
            var path = $"/api/vms/{Uri.EscapeDataString(vmName)}/ip{identityQuery}{separator}rdpPort={rdpTargetPort}";
            var response = interfaces.Count == 0
                ? await client.GetAsync(path, deadline.Token)
                : await client.PostAsync(path,
                    new StringContent(JsonSerializer.Serialize(new { interfaces }), Encoding.UTF8,
                        "application/json"), deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Agent VM IP lookup failed on node {NodeId}: {Status}",
                    nodeId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AgentVmIpResponse>(deadline.Token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
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
        _ = await DeleteDockerImageWithInventoryAsync(nodeId, image, token);
    }

    public virtual async Task<AgentImageCacheCleanupResult> DeleteDockerImageWithInventoryAsync(
        Guid nodeId,
        string image,
        CancellationToken token)
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
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return AgentImageCacheCleanupResult.Clean;
        return await response.Content.ReadFromJsonAsync<AgentImageCacheCleanupResult>(token)
               ?? throw InvalidAgentResponse(node.Id, "image.docker.delete", "Agent returned an empty image cache inventory.");
    }

    public virtual async Task DeleteVmImageAsync(Guid nodeId, int templateId, string hash, CancellationToken token)
    {
        _ = await DeleteVmImageWithInventoryAsync(nodeId, templateId, hash, token);
    }

    public virtual async Task<AgentImageCacheCleanupResult> DeleteVmImageWithInventoryAsync(
        Guid nodeId,
        int templateId,
        string hash,
        CancellationToken token)
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
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return AgentImageCacheCleanupResult.Clean;
        return await response.Content.ReadFromJsonAsync<AgentImageCacheCleanupResult>(token)
               ?? throw InvalidAgentResponse(node.Id, "image.vm.delete", "Agent returned an empty image cache inventory.");
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

    public virtual async Task<AgentRemoteRelayResponse> CreateRemoteRelayAsync(
        Guid nodeId,
        AgentRemoteRelayRequest request,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token) ?? throw NodeNotFound(nodeId, "remote_access.relay.create");
        var client = BuildClient(node);
        var response = await client.PostAsJsonAsync("/api/remote-access/relays", request, token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(response, "remote_access.relay.create", node.Id,
                $"Agent remote relay creation failed on node {node.Name} ({node.HostAddress}).", token);
        return await response.Content.ReadFromJsonAsync<AgentRemoteRelayResponse>(token)
               ?? throw new AgentClientException(new OperationalError(OperationalErrorCategory.AgentProtocol,
                   "remote_access.empty_response", "Agent returned an empty remote relay response.", false));
    }

    public virtual async Task DeleteRemoteRelayAsync(Guid nodeId, Guid sessionId, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token) ?? throw NodeNotFound(nodeId, "remote_access.relay.delete");
        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/remote-access/relays/{sessionId:D}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw await CreateAgentExceptionAsync(response, "remote_access.relay.delete", node.Id,
                $"Agent remote relay cleanup failed on node {node.Name} ({node.HostAddress}).", token);
    }

    public virtual async Task CancelRemoteTerminalAsync(Guid nodeId, Guid sessionId, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token) ?? throw NodeNotFound(nodeId, "remote_access.terminal.cancel");
        var client = BuildClient(node);
        var response = await client.DeleteAsync($"/api/remote-access/terminals/{sessionId:D}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw await CreateAgentExceptionAsync(response, "remote_access.terminal.cancel", node.Id,
                $"Agent terminal cancellation failed on node {node.Name} ({node.HostAddress}).", token);
    }

    public virtual async Task<AgentVmImageDownloadResult> DownloadPreparedVmImageAsync(
        Guid nodeId,
        int templateId,
        string hash,
        long expectedSize,
        string registryAddress,
        string repository,
        string tag,
        CancellationToken token = default)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return AgentVmImageDownloadResult.Failed($"Fleet node {nodeId} was not found.");
        var digest = NormalizeSha256Digest(hash);
        var body = JsonSerializer.Serialize(new
        {
            templateId,
            hash = digest,
            expectedSize,
            registryAddress,
            repository,
            tag,
            digest = $"sha256:{digest}"
        });
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromHours(2));
        using var response = await client.PostAsync(
            "/api/images/download-vm",
            new StringContent(body, Encoding.UTF8, "application/json"), deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadAgentErrorAsync(
                response,
                "image.vm.download-prepared",
                node.Id,
                $"Agent prepared VM image download failed on node {node.Name} ({node.HostAddress}) for template {templateId}.",
                token);
            return AgentVmImageDownloadResult.Failed(error.Message);
        }
        return await response.Content.ReadFromJsonAsync<AgentVmImageDownloadResult>(token)
               ?? AgentVmImageDownloadResult.Failed(
                   $"Agent returned an empty prepared VM image response on node {node.Name} ({node.HostAddress}).");
    }

    public virtual async Task<AgentVmImagePublishResult> PublishVmImageAsync(
        Guid nodeId,
        int templateId,
        string hash,
        long expectedSize,
        VmImageArtifactReference registryReference,
        CancellationToken token = default)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return AgentVmImagePublishResult.Failed($"Fleet node {nodeId} was not found.");
        var digest = NormalizeSha256Digest(hash);
        var body = JsonSerializer.Serialize(new
        {
            templateId,
            hash = digest,
            expectedSize,
            registryTarget = new
            {
                registryAddress = registryReference.RegistryAddress,
                repository = registryReference.Repository,
                tag = registryReference.Tag
            }
        });
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromHours(2));
        using var response = await client.PostAsync(
            "/api/images/publish-vm",
            new StringContent(body, Encoding.UTF8, "application/json"), deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadAgentErrorAsync(
                response,
                "image.vm.publish",
                node.Id,
                $"Agent VM image publication failed on node {node.Name} ({node.HostAddress}) for template {templateId}.",
                token);
            return AgentVmImagePublishResult.Failed(error.Message);
        }
        return await response.Content.ReadFromJsonAsync<AgentVmImagePublishResult>(token)
               ?? AgentVmImagePublishResult.Failed(
                   $"Agent returned an empty VM image publication response on node {node.Name} ({node.HostAddress}).");
    }

    private static string BuildVmIdentityQuery(int? expectedGeneration, string? expectedNativeId)
    {
        var query = new List<string>(2);
        if (expectedGeneration is { } generation)
            query.Add($"generation={generation}");
        if (!string.IsNullOrWhiteSpace(expectedNativeId))
            query.Add($"nativeId={Uri.EscapeDataString(expectedNativeId)}");
        return query.Count == 0 ? string.Empty : $"?{string.Join('&', query)}";
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

    public virtual async Task<AgentBootstrapArtifactDownloadResult> DownloadBootstrapArtifactAsync(
        Guid nodeId,
        AgentBootstrapArtifactDownloadRequest request,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null)
            return AgentBootstrapArtifactDownloadResult.Failed($"Fleet node {nodeId} was not found.");
        var client = BuildClient(node);
        using var deadline = CreateDeadline(token, TimeSpan.FromHours(2));
        using var response = await client.PostAsJsonAsync(
            "/api/images/download-bootstrap-artifact", request, deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw await CreateAgentExceptionAsync(
                response,
                "bootstrap.artifact.download",
                node.Id,
                $"Agent bootstrap artifact download failed on node {node.Name} ({node.HostAddress}).",
                token);
        return await response.Content.ReadFromJsonAsync<AgentBootstrapArtifactDownloadResult>(token)
               ?? AgentBootstrapArtifactDownloadResult.Failed("Agent returned an empty bootstrap artifact response.");
    }

    public virtual async Task DeleteBootstrapArtifactAsync(
        Guid nodeId,
        Guid profileId,
        int version,
        CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) throw NodeNotFound(nodeId, "bootstrap.artifact.delete");
        var client = BuildClient(node);
        using var response = await client.DeleteAsync(
            $"/api/images/bootstrap-artifact/{profileId:D}/{version}", token);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw await CreateAgentExceptionAsync(
                response,
                "bootstrap.artifact.delete",
                node.Id,
                $"Agent bootstrap artifact cleanup failed on node {node.Name} ({node.HostAddress}).",
                token);
    }
}

public class AgentCreateContainerResponse
{
    public string ContainerId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int PublicPort { get; set; }
}

public class AgentClientException : Exception, IOperationalFailureException
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

public sealed record AgentRemoteRelayRequest(
    Guid SessionId,
    int RuntimeId,
    int Generation,
    string VmName,
    string NativeId,
    string TargetAddress,
    int TargetPort,
    DateTimeOffset ExpiresAt);

public sealed record AgentRemoteRelayResponse(Guid SessionId, int Port, DateTimeOffset ExpiresAt);

public class AgentCreateVmRequest
{
    public Guid? OperationId { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public int GuestReadyWarningAfterSeconds { get; set; } = 180;
    public int? TemplateId { get; set; }
    public string? TemplatePath { get; set; }
    public bool ImageEnsured { get; set; }
    public string VmName { get; set; } = string.Empty;
    public int Memory { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    public string DefaultNetworkModel { get; set; } = "e1000e";
    public string? Flag { get; set; }
    public List<AgentVmNetworkInterfaceRequest> Interfaces { get; set; } = [];
    public AgentVmInitConfig? CloudInit { get; set; }
    public AgentVmGuestControlConfig GuestControl { get; set; } = new();
    public AgentVmManagementInterfaceConfig? ManagementInterface { get; set; }
    public AgentVmGuestSupervisorConfig? GuestSupervisor { get; set; }
}

public sealed class AgentVmManagementInterfaceConfig
{
    public GuestAssetIdentity? Identity { get; set; }
    public string BridgeName { get; set; } = "gzmgt0";
    public string MacAddress { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int PrefixLength { get; set; } = 16;
    public string HostAddress { get; set; } = "100.127.0.1";
    public string Model { get; set; } = "e1000e";
}

public sealed class AgentVmGuestSupervisorConfig
{
    public GuestAssetIdentity Identity { get; set; } = null!;
    public string EnrollmentToken { get; set; } = string.Empty;
    public string WorkerServerCertificateSha256 { get; set; } = string.Empty;
    public string EnrollmentEndpoint { get; set; } = string.Empty;
    public string IntentDigest { get; set; } = string.Empty;
}

public sealed record AgentVmImageRegistryTarget(string RegistryAddress, string Repository, string Tag);

public sealed record AgentCommitVmScenarioRequest(
    Guid OperationId,
    string VmName,
    OSType OsType,
    string BuildIdentity,
    AgentVmImageRegistryTarget RegistryTarget);

public sealed record AgentCommitVmScenarioResponse(
    bool Success,
    string ArtifactDigest,
    long ArtifactSize,
    string EvidenceDigest,
    string RegistryAddress,
    string Repository,
    string Tag,
    string? ErrorCode = null,
    string? ErrorDetail = null);

public sealed record AgentGuestConformancePackageRequest(
    Guid ProfileId,
    int Version,
    string ArtifactDigest,
    string ArtifactBase64);

public sealed record AgentVmCleanShutdownResponse(bool CleanShutdown);

public sealed class AgentVmGuestControlConfig
{
    public bool Enabled { get; set; } = true;
    public bool Required { get; set; } = true;
    public bool EndpointSensorChannel { get; set; }
    public OSType? OsType { get; set; }
}

public class AgentVmNetworkInterfaceRequest
{
    public string BridgeName { get; set; } = string.Empty;
    public string? HostInterfaceName { get; set; }
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
    public VmNetworkMode NetworkMode { get; set; } = VmNetworkMode.Dhcp;
    public string UserData { get; set; } = string.Empty;
    public string MetaData { get; set; } = string.Empty;
    public string NetworkConfig { get; set; } = string.Empty;
    public List<string> SensitiveKeys { get; set; } = [];
}

public sealed record AgentVmBootstrapApplyRequest(
    Guid? OperationId,
    int RuntimeId,
    int Generation,
    string AssetKey,
    OSType OsType,
    Guid? ProfileId,
    int? ProfileVersion,
    string? ArtifactDigest,
    long? ArtifactSize,
    string? ManifestJson,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyList<AgentVmNetworkInterfaceRequest> Interfaces,
    bool RunHealthChecks = true);

public sealed record AgentVmBootstrapApplyResponse(
    bool Success,
    string Stage,
    string Message,
    int RebootCount,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> PassedHealthChecks,
    string? ErrorCode = null,
    string? FailedStep = null,
    string? FailureCategory = null,
    int? ExitCode = null);

public sealed record AgentVmGuestStatusResponse(
    bool Ready,
    string Message,
    string? Version);

public sealed record AgentVmCapabilityProbeRequest(
    OSType OsType,
    IReadOnlyList<string> Capabilities,
    string? ExpectedMarkerPath = null,
    string? ExpectedMarkerValue = null,
    int TimeoutSeconds = 180);

public sealed record AgentVmCapabilityProbeResponse(
    bool Success,
    IReadOnlyList<string> VerifiedCapabilities,
    IReadOnlyDictionary<string, string> Evidence,
    string? ErrorCode,
    string? ErrorDetail);

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
    string? LinuxSensorDownloadUrl = null,
    string? LinuxSensorSha256 = null,
    string? WindowsSensorDownloadUrl = null,
    string? WindowsSensorSha256 = null,
    AgentVmControlPlaneSyncConfig? VmControlPlane = null,
    TeamLabDataPlaneSyncConfig? TeamLabDataPlane = null,
    bool Restart = true);

public sealed record AgentVmControlPlaneSyncConfig(
    bool Enabled,
    string BridgeName = "gzmgt0",
    string HostAddress = "100.127.0.1",
    int PrefixLength = 16,
    int ListenPort = 5443,
    string GuestStateRoot = "/var/lib/gzctf/teamlab/guest-control");

public sealed record TeamLabDataPlaneSyncConfig(
    bool Enabled,
    TeamLabExecutionModel ExecutionModel,
    bool ControlPlane,
    string? NorthboundEndpoint,
    string? SouthboundEndpoint,
    string? NorthboundListenEndpoint,
    string? SouthboundListenEndpoint,
    string? ChassisEncapIp,
    string IntegrationBridgeName = "br-int",
    int ManagedDhcpLeaseSeconds = 3600);

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

public sealed record AgentImageCacheCleanupResult(
    IReadOnlyList<AgentImageCacheInventoryEntry> Inventory,
    int Removed = 0)
{
    public static readonly AgentImageCacheCleanupResult Clean = new([], 0);

    public bool IsClean => Inventory.All(item => !item.Present);
}

public sealed record AgentImageCacheInventoryEntry(string Kind, string Identity, bool Present);

public sealed record AgentVmImagePublishResult(
    bool Success,
    bool Verified,
    long Size,
    string? Digest,
    string? ManifestDigest,
    string? Message = null)
{
    public static AgentVmImagePublishResult Failed(string message) =>
        new(false, false, 0, null, null, message);
}

public sealed record AgentBootstrapArtifactDownloadRequest(
    Guid ProfileId,
    int Version,
    string RegistryAddress,
    string Repository,
    string Digest,
    long ExpectedSize);

public sealed record AgentBootstrapArtifactDownloadResult(
    bool Success,
    string Message,
    bool AlreadyExists,
    bool Verified,
    string? LocalPath,
    long Size,
    string Digest)
{
    public static AgentBootstrapArtifactDownloadResult Failed(string message) =>
        new(false, message, false, false, null, 0, string.Empty);
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
    string? Message = null,
    string? FabricInterfaceName = null,
    string? FabricIp = null,
    bool FabricReady = false);

public record TeamLabToolCapabilityReport(
    bool Docker,
    bool Kvm,
    bool KvmDevice,
    bool CpuVirtualization,
    bool WireGuard,
    bool Iptables,
    bool Nftables,
    bool Tcpdump,
    bool Dumpcap,
    bool DnsProbe = false,
    bool OvsVsctl = false,
    bool OvsdbClient = false,
    bool OvnController = false,
    bool OvnNorthboundClient = false,
    bool OvnSouthboundClient = false);

public record TeamLabDryRunResponse(
    bool Success,
    bool DryRun,
    string Message,
    string[] Commands);

public record TeamLabManagedSwitchIntent(
    string Key,
    string Name,
    string Cidr,
    string GatewayIp,
    string BridgeName,
    string DhcpDnsServiceName,
    TeamLabDhcpLeaseRequest[] Records,
    TeamLabDnsRecordRequest[]? DnsRecords = null);

public record TeamLabManagedRouterFragmentIntent(
    string Key,
    string[] NetworkKeys);

public record TeamLabFabricUplinkIntent(
    string FabricIp,
    string HubAddressCidr,
    string NodeAddressCidr,
    string HostInterfaceName,
    string NamespaceInterfaceName,
    TeamLabStaticRouteRequest[] LocalRoutes,
    TeamLabStaticRouteRequest[] RemoteRoutes);

public record TeamLabObservationPointIntent(
    Guid PublicId,
    string TopologyKey,
    byte Kind,
    string InterfaceToken);

public record TeamLabInfrastructureApplyRequest(
    int RuntimeId,
    int Generation,
    int RouteVersion,
    string RouterNamespace,
    TeamLabManagedSwitchIntent[] Switches,
    TeamLabManagedRouterFragmentIntent[] Routers,
    TeamLabFabricUplinkIntent Fabric,
    TeamLabForwardPolicyRequest[] ForwardPolicies,
    TeamLabObservationPointIntent[] ObservationPoints,
    bool DryRun = true);

public record TeamLabInfrastructureResourceFact(
    string Kind,
    string Key,
    string NativeIdentity,
    string Status);

public record TeamLabInfrastructureApplyResponse(
    bool Success,
    bool DryRun,
    string Message,
    string? DesiredStateDigest,
    bool AlreadyApplied,
    TeamLabInfrastructureResourceFact[] Resources,
    string[] Commands);

public record TeamLabInfrastructureStateResponse(
    bool Exists,
    int RuntimeId,
    int Generation,
    int RouteVersion,
    string? DesiredStateDigest,
    TeamLabInfrastructureResourceFact[] Resources,
    DateTimeOffset? AppliedAt);

public record TeamLabStaticRouteRequest(
    string TargetCidr,
    string GatewayIp,
    string SourceIp = "");

public record TeamLabWireGuardRequest(
    int RuntimeId,
    int Generation,
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
    bool DryRun = true,
    TeamLabExecutionModel ExecutionModel = TeamLabExecutionModel.V1,
    Guid RuntimePublicId = default,
    string? NetworkKey = null,
    string? PortKey = null,
    string? MacAddress = null);

public record TeamLabWireGuardCleanupRequest(
    int RuntimeId,
    int Generation,
    string NamespaceName,
    string InterfaceName,
    bool DryRun = true,
    TeamLabExecutionModel ExecutionModel = TeamLabExecutionModel.V1,
    Guid RuntimePublicId = default,
    string? NetworkKey = null);

public record TeamLabCleanupRequest(
    int RuntimeId,
    int Generation,
    string RouterNamespace,
    string[] ResourceNames,
    string[] SensorAssetKeys,
    string[] FabricRemoteCidrs,
    bool DryRun = true);

public record TeamLabAssetLifecycleRequest(
    string Kind,
    string ResourceId,
    int Generation,
    bool DryRun = false,
    TeamLabExecutionModel ExecutionModel = TeamLabExecutionModel.V1);

public record TeamLabAssetLifecycleResponse(
    bool Success,
    bool DryRun,
    string State,
    string Message);

public record TeamLabProbeRequest(
    int RuntimeId,
    string NamespaceName,
    string TargetIp,
    string? Kind = null,
    int? Port = null,
    bool DryRun = true)
{
    public TeamLabProbeRequest(int runtimeId, string namespaceName, string targetIp, bool dryRun)
        : this(runtimeId, namespaceName, targetIp, null, null, dryRun)
    {
    }
}

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
    string Hostname,
    bool IsPrimary = true);

public record TeamLabDnsRecordRequest(
    string Hostname,
    string IpAddress);

public record TeamLabForwardPolicyRequest(
    string SourceCidr,
    string DestinationCidr,
    bool Allow);

public record TeamLabCaptureStartRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    Guid ObservationPointId,
    string InterfaceToken,
    int MaxSeconds,
    long MaxBytes,
    bool DryRun = true);

public record TeamLabContainerInterfaceExpectation(
    string Name,
    string AddressCidr,
    string MacAddress);

public record TeamLabContainerRouteExpectation(
    string TargetCidr,
    string? GatewayIp,
    string InterfaceName);

public record TeamLabContainerDnsProbeExpectation(
    string Server,
    string QueryName,
    string ExpectedAddress);

public record TeamLabContainerNetworkFinalizeRequest(
    Guid OperationId,
    int RuntimeId,
    int Generation,
    string ContainerId,
    string ContainerName,
    TeamLabContainerInterfaceExpectation[] Interfaces,
    TeamLabContainerRouteExpectation[] Routes,
    string[] DnsServers,
    TeamLabContainerDnsProbeExpectation[] DnsProbes,
    bool RequireNoDefaultRoute,
    bool DryRun = false);

public record TeamLabContainerNetworkFinalizeResponse(
    bool Success,
    bool DryRun,
    string Message,
    bool AlreadyFinalized,
    string[] Commands);

public record TeamLabCaptureStopRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    bool DryRun = true);

public record TeamLabCaptureStatusRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    bool DryRun = true);

public record TeamLabCaptureDeleteRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    bool DryRun = true);

public record TeamLabCaptureUploadRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    string UploadPath,
    string UploadToken,
    long MaxBytes,
    bool DryRun = true);

public record TeamLabCaptureResponse(
    bool Success,
    bool DryRun,
    string Message,
    Guid SegmentId,
    string? FilePath,
    long CapturedBytes,
    bool Running,
    string? Sha256,
    bool Uploaded,
    string[] Commands);

/// <summary>Applies a link/netem policy on a runtime link's host-side veth.</summary>
public record TeamLabLinkPolicyApplyRequest(
    Guid RuntimePublicId,
    int Generation,
    string NetworkKey,
    string AssetKey,
    string Kind,
    string ParametersJson,
    bool DryRun = false,
    int RuntimeId = 0,
    string? RouterNamespace = null,
    string? NetworkCidr = null,
    string? GatewayIp = null);

/// <summary>Recovers (removes) a link policy on a runtime link's host-side veth.</summary>
public record TeamLabLinkPolicyRecoverRequest(
    Guid RuntimePublicId,
    int Generation,
    string NetworkKey,
    string AssetKey,
    string Kind,
    bool DryRun = false,
    int RuntimeId = 0,
    string? RouterNamespace = null,
    string? NetworkCidr = null,
    string? GatewayIp = null,
    string? ParametersJson = null);

public record TeamLabLinkPolicyResponse(
    bool Success,
    bool DryRun,
    string Interface,
    string State,
    string Message);

public enum TeamLabObservationEvidenceKind : byte
{
    Packet = 0,
    EndpointProcess = 1
}

public record TeamLabObservationBatchRequest(
    int RuntimeId,
    int Generation,
    long AfterSequence = 0,
    Guid? ObservationPointId = null,
    int Limit = 500,
    long AcknowledgeThroughSequence = 0);

public record TeamLabObservationRecord(
    long Sequence,
    Guid? ObservationPointId,
    string? AssetKey,
    DateTimeOffset CapturedAt,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    byte? TcpFlags,
    int PacketLength,
    string? PacketFingerprint,
    string FlowFingerprint,
    TeamLabObservationEvidenceKind EvidenceKind,
    string? ProcessIdentityHash = null,
    string Direction = "observed",
    DateTimeOffset? FirstSeenAt = null,
    DateTimeOffset? LastSeenAt = null,
    long Packets = 1,
    long? Bytes = null);

public record TeamLabObservationHealth(
    bool Running,
    int RegisteredPointCount,
    int ActiveInterfaceCount,
    int ActiveFlowCount,
    long DroppedCount,
    long ParserFailureCount,
    long SensorRejectedCount,
    long SpoolBytes,
    string? LastSensorErrorCode,
    string? LastError);

public record TeamLabObservationBatchResponse(
    bool Success,
    string Message,
    long NextSequence,
    long DroppedCount,
    long PersistedThroughSequence,
    TeamLabObservationRecord[] Records,
    TeamLabObservationHealth Health);

public enum TeamLabEndpointSensorChannelMode : byte
{
    Vm = 0,
    Docker = 1
}

public record TeamLabEndpointSensorRegistrationRequest(
    int RuntimeId,
    string RuntimePublicId,
    int Generation,
    string AssetKey,
    string RuntimeResourceId,
    int SensorVersion,
    string HmacKeyBase64,
    TeamLabEndpointSensorChannelMode Mode);

public record TeamLabEndpointSensorRemoveRequest(
    int RuntimeId,
    int Generation,
    string AssetKey);

public record TeamLabEndpointSensorStartRequest(
    int RuntimeId,
    int Generation,
    string AssetKey,
    string RuntimeResourceId,
    TeamLabEndpointSensorChannelMode Mode,
    OSType? OsType = null);

public record TeamLabEndpointSensorResponse(
    bool Success,
    string Message,
    string? ChannelEndpoint = null);
