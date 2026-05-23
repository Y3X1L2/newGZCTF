using Docker.DotNet;
using Docker.DotNet.Models;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public class DockerService
{
    private readonly DockerClient _client;
    private readonly DockerConfig _config;
    private readonly ILogger<DockerService> _logger;

    public DockerService(IOptions<DockerConfig> config, ILogger<DockerService> logger)
    {
        _config = config.Value;
        _logger = logger;
        _client = new DockerClientConfiguration(new Uri(_config.Uri)).CreateClient();
    }

    public async Task<AgentContainerResponse?> CreateContainerAsync(CreateContainerRequest request, CancellationToken token)
    {
        await EnsureNetworkAsync(token);

        var containerName = $"gzctf_c{request.ChallengeId}_u{request.UserId.ToString("N")[..8]}";
        var portSpec = $"{request.ExposedPort}/tcp";

        var createParams = new CreateContainerParameters
        {
            Name = containerName,
            Image = request.Image,
            Env = new List<string> { $"GZCTF_FLAG={request.Flag}" },
            Labels = new Dictionary<string, string>
            {
                ["ChallengeId"] = request.ChallengeId.ToString(),
                ["TeamId"] = request.TeamId,
                ["UserId"] = request.UserId.ToString(),
                ["ManagedBy"] = "GZCTF"
            },
            HostConfig = new HostConfig
            {
                Memory = request.MemoryLimit * 1024L * 1024,
                CPUPercent = request.CPUCount * 10,
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    [portSpec] = new List<PortBinding> { new() { HostPort = "0" } }
                },
                NetworkMode = _config.ChallengeNetwork,
            },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                [portSpec] = new()
            },
        };

        Docker.DotNet.Models.CreateContainerResponse? createResult;
        try
        {
            createResult = await _client.Containers.CreateContainerAsync(createParams, token);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Image {Image} not found, pulling...", request.Image);
            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = request.Image }, null,
                new Progress<JSONMessage>(), token);
            createResult = await _client.Containers.CreateContainerAsync(createParams, token);
        }

        await _client.Containers.StartContainerAsync(createResult.ID, new ContainerStartParameters(), token);

        var inspect = await _client.Containers.InspectContainerAsync(createResult.ID, token);
        var network = inspect.NetworkSettings.Networks.TryGetValue(_config.ChallengeNetwork, out var netVal) ? netVal : null;
        var portBinding = inspect.NetworkSettings.Ports.TryGetValue(portSpec, out var pbVal) ? pbVal?.FirstOrDefault() : null;

        return new AgentContainerResponse
        {
            ContainerId = createResult.ID,
            IP = network?.IPAMConfig?.IPv4Address ?? network?.IPAddress ?? "",
            Port = request.ExposedPort,
            PublicPort = int.TryParse(portBinding?.HostPort, out var pp) ? pp : 0,
        };
    }

    public async Task DestroyContainerAsync(string containerId, CancellationToken token)
    {
        try { await _client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, token); } catch { }
        try { await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, token); } catch { }
    }

    public async Task<int> GetContainerCountAsync(CancellationToken token)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { ["ManagedBy=GZCTF"] = true }
            },
            All = false
        }, token);
        return containers.Count;
    }

    public async Task PullImageAsync(string image, string? registryAuth, CancellationToken token)
    {
        AuthConfig? authConfig = null;
        if (!string.IsNullOrEmpty(registryAuth))
        {
            try
            {
                authConfig = System.Text.Json.JsonSerializer.Deserialize<AuthConfig>(
                    Convert.FromBase64String(registryAuth));
            }
            catch { /* ignore invalid auth */ }
        }

        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = image },
            authConfig,
            new Progress<JSONMessage>(), token);
    }

    private async Task EnsureNetworkAsync(CancellationToken token)
    {
        try
        {
            await _client.Networks.InspectNetworkAsync(_config.ChallengeNetwork, token);
        }
        catch (DockerApiException)
        {
            await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = _config.ChallengeNetwork,
                Driver = "bridge",
                Labels = new Dictionary<string, string> { ["ManagedBy"] = "GZCTF" }
            }, token);
        }
    }
}
