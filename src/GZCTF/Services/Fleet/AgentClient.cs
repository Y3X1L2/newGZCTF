using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;

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
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", node.AuthToken);
        return client;
    }

    private async Task<WorkerNode?> GetNodeAsync(Guid nodeId, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        return await repo.GetNodeByIdAsync(nodeId, token);
    }

    public async Task<AgentCreateContainerResponse?> CreateContainerAsync(Guid nodeId, ContainerConfig config, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return null;

        var client = BuildClient(node);
        var body = JsonSerializer.Serialize(new
        {
            config.Image, config.TeamId, config.ChallengeId, config.UserId,
            config.ExposedPort, config.Flag, config.EnableTrafficCapture,
            config.MemoryLimit, config.CPUCount, config.StorageLimit,
            NetworkMode = config.NetworkMode.ToString()
        });
        var response = await client.PostAsync("/api/containers/create", new StringContent(body, Encoding.UTF8, "application/json"), token);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Agent create container failed on node {NodeId}: {Status}", nodeId, response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<AgentCreateContainerResponse>(token);
    }

    public async Task DestroyContainerAsync(Guid nodeId, string containerId, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return;

        var client = BuildClient(node);
        try
        {
            await client.DeleteAsync($"/api/containers/{containerId}", token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent destroy container failed on node {NodeId}", nodeId);
        }
    }

    public async Task<AgentCreateVmResponse?> CreateVmAsync(Guid nodeId, AgentCreateVmRequest request, CancellationToken token)
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

    public async Task DestroyVmAsync(Guid nodeId, string vmName, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return;

        var client = BuildClient(node);
        try
        {
            await client.DeleteAsync($"/api/vms/{vmName}", token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent destroy VM failed on node {NodeId}", nodeId);
        }
    }

    public async Task<AgentVmIpResponse?> GetVmIpAsync(Guid nodeId, string vmName, CancellationToken token)
    {
        var node = await GetNodeAsync(nodeId, token);
        if (node is null) return null;

        var client = BuildClient(node);
        try
        {
            var response = await client.GetAsync($"/api/vms/{Uri.EscapeDataString(vmName)}/ip", token);
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
            _logger.LogWarning("Agent Docker image pull failed on node {NodeId}: {Status}", nodeId,
                response.StatusCode);
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
            _logger.LogWarning("Agent VM image download failed on node {NodeId}: {Status}", nodeId,
                response.StatusCode);
    }
}

public class AgentCreateContainerResponse
{
    public string ContainerId { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int PublicPort { get; set; }
}

public class AgentCreateVmRequest
{
    public int? TemplateId { get; set; }
    public string VmName { get; set; } = string.Empty;
    public int Memory { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    public string? Flag { get; set; }
}

public class AgentCreateVmResponse
{
    public string VmName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? VncAddress { get; set; }
}

public class AgentVmIpResponse
{
    public string VmName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int? RdpPort { get; set; }
    public string Status { get; set; } = string.Empty;
}
