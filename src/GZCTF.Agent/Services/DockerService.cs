using Docker.DotNet;
using Docker.DotNet.Models;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

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
        var attachments = GetNetworkAttachments(request);
        var primaryAttachment = attachments.First();
        var primaryNetwork = primaryAttachment.NetworkName;

        foreach (var attachment in attachments)
            await EnsureNetworkAsync(attachment, token);

        var containerName = BuildContainerName(request);
        var portSpec = $"{request.ExposedPort}/tcp";
        var env = request.EnvironmentVariables
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToList();

        env.Add($"GZCTF_TEAM_ID={request.TeamId}");
        if (request.Flag is not null)
            env.Add($"GZCTF_FLAG={request.Flag}");

        var createParams = new CreateContainerParameters
        {
            Name = containerName,
            Image = request.Image,
            Env = env,
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
                PortBindings = request.PublishPort
                    ? new Dictionary<string, IList<PortBinding>>
                    {
                        [portSpec] = new List<PortBinding> { new() { HostPort = ResolveHostPortBinding() } }
                    }
                    : null,
                NetworkMode = primaryNetwork,
            },
            ExposedPorts = request.PublishPort ? new Dictionary<string, EmptyStruct> { [portSpec] = new() } : null,
            NetworkingConfig = !string.IsNullOrWhiteSpace(primaryAttachment.IPAddress)
                ? new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [primaryNetwork] = new()
                        {
                            IPAMConfig = new EndpointIPAMConfig { IPv4Address = primaryAttachment.IPAddress }
                        }
                    }
                }
                : null,
        };

        if (!string.IsNullOrWhiteSpace(request.StartCommand))
            createParams.Cmd = ["sh", "-c", request.StartCommand];

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

        foreach (var attachment in attachments
                     .Where(n => !n.NetworkName.Equals(primaryNetwork, StringComparison.Ordinal))
                     .DistinctBy(n => n.NetworkName))
        {
            try
            {
                await _client.Networks.ConnectNetworkAsync(attachment.NetworkName,
                    new NetworkConnectParameters
                    {
                        Container = createResult.ID,
                        EndpointConfig = string.IsNullOrWhiteSpace(attachment.IPAddress)
                            ? null
                            : new EndpointSettings
                            {
                                IPAMConfig = new EndpointIPAMConfig { IPv4Address = attachment.IPAddress }
                            }
                    }, token);
            }
            catch (DockerApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogDebug(ex, "Container {ContainerId} already connected to {NetworkName}",
                    createResult.ID, attachment.NetworkName);
            }
        }

        var inspect = await _client.Containers.InspectContainerAsync(createResult.ID, token);
        var network = inspect.NetworkSettings.Networks.TryGetValue(primaryNetwork, out var netVal) ? netVal : null;
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

    public async Task RemoveNetworkAsync(string networkName, CancellationToken token)
    {
        try
        {
            var network = await _client.Networks.InspectNetworkAsync(networkName, token);
            await _client.Networks.DeleteNetworkAsync(network.ID, token);
        }
        catch (DockerApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug(ex, "Docker network {NetworkName} is already absent", networkName);
        }
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

    private string ResolveHostPortBinding()
    {
        var start = _config.PublicPortStart;
        var end = _config.PublicPortEnd;

        if (start is null || end is null || start <= 0 || end < start || end > ushort.MaxValue)
            return "0";

        for (var port = start.Value; port <= end.Value; port++)
        {
            if (IsTcpPortAvailable(port))
                return port.ToString();
        }

        _logger.LogWarning(
            "No available Docker public port in configured range {Start}-{End}; falling back to Docker random port",
            start, end);
        return "0";
    }

    static bool IsTcpPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task EnsureNetworkAsync(ContainerNetworkAttachment attachment, CancellationToken token)
    {
        try
        {
            await _client.Networks.InspectNetworkAsync(attachment.NetworkName, token);
        }
        catch (DockerApiException)
        {
            var parameters = new NetworksCreateParameters
            {
                Name = attachment.NetworkName,
                Driver = "bridge",
                Internal = attachment.IsInternal,
                Options = new Dictionary<string, string>
                {
                    ["com.docker.network.bridge.enable_icc"] =
                        attachment.EnableInterContainerCommunication ? "true" : "false"
                },
                Labels = new Dictionary<string, string> { ["ManagedBy"] = "GZCTF" }
            };

            if (!string.IsNullOrWhiteSpace(attachment.SubnetCidr))
            {
                parameters.IPAM = new IPAM
                {
                    Config = [new IPAMConfig { Subnet = attachment.SubnetCidr }]
                };
            }

            await _client.Networks.CreateNetworkAsync(parameters, token);
        }
    }

    private List<ContainerNetworkAttachment> GetNetworkAttachments(CreateContainerRequest request)
    {
        if (request.NetworkAttachments.Count > 0)
        {
            var normalized = request.NetworkAttachments
                .Where(n => !string.IsNullOrWhiteSpace(n.NetworkName))
                .Select(n => new ContainerNetworkAttachment
                {
                    NetworkName = n.NetworkName.Trim(),
                    SubnetCidr = string.IsNullOrWhiteSpace(n.SubnetCidr) ? null : n.SubnetCidr.Trim(),
                    IPAddress = string.IsNullOrWhiteSpace(n.IPAddress) ? null : n.IPAddress.Trim(),
                    IsPrimary = n.IsPrimary,
                    IsInternal = n.IsInternal,
                    EnableInterContainerCommunication = n.EnableInterContainerCommunication
                })
                .DistinctBy(n => n.NetworkName)
                .ToList();

            if (normalized.Count > 0)
            {
                if (normalized.All(n => !n.IsPrimary))
                    normalized[0].IsPrimary = true;
                return normalized.OrderByDescending(n => n.IsPrimary).ToList();
            }
        }

        var primaryNetwork = string.IsNullOrWhiteSpace(request.NetworkName)
            ? _config.ChallengeNetwork
            : request.NetworkName.Trim();
        var attachments = new List<ContainerNetworkAttachment>
        {
            new()
            {
                NetworkName = primaryNetwork,
                SubnetCidr = request.NetworkSubnets.GetValueOrDefault(primaryNetwork),
                IPAddress = request.IPAddress,
                IsPrimary = true
            }
        };

        foreach (var networkName in request.AdditionalNetworkNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            if (networkName == primaryNetwork)
                continue;

            attachments.Add(new ContainerNetworkAttachment
            {
                NetworkName = networkName,
                SubnetCidr = request.NetworkSubnets.GetValueOrDefault(networkName),
                IsPrimary = false
            });
        }

        return attachments;
    }

    public static string BuildContainerName(CreateContainerRequest request)
    {
        var fingerprint = string.Join('|',
            request.ChallengeId,
            request.TeamId,
            request.UserId.ToString("N"),
            request.ExposedPort,
            request.Flag ?? string.Empty);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..12].ToLowerInvariant();
        return $"gzctf_c{request.ChallengeId}_t{SanitizeNamePart(request.TeamId)}_{hash}";
    }

    private static string SanitizeNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "none";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                builder.Append(ch);
        }

        return builder.Length == 0 ? "none" : builder.ToString()[..Math.Min(builder.Length, 32)];
    }
}
