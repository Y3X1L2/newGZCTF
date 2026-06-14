using System.Net;
using System.IO.Compression;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Provider;
using ContainerStatus = GZCTF.Utils.ContainerStatus;

namespace GZCTF.Services.Container.Manager;

public class DockerManager : IContainerManager, IContainerPatchApplicator
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerManager> _logger;
    private readonly DockerMetadata _meta;
    private readonly bool _isWindowsDaemon;
    private static readonly TimeSpan PatchApplyTimeout = TimeSpan.FromSeconds(60);
    private const int MaxExtractedPatchArchiveSize = 64 * 1024 * 1024;

    public DockerManager(IContainerProvider<DockerClient, DockerMetadata> provider, ILogger<DockerManager> logger)
    {
        _logger = logger;
        _meta = provider.GetMetadata();
        _client = provider.GetProvider();
        _isWindowsDaemon = IsWindowsDockerDaemon();

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.ContainerManager_DockerMode)],
            TaskStatus.Success, LogLevel.Debug);
    }


    public async Task DestroyContainerAsync(Models.Data.Container container, CancellationToken token = default)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(container.ContainerId,
                new() { Force = true }, token);
        }
        catch (DockerContainerNotFoundException)
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDestroyed),
                    container.LogId],
                TaskStatus.Success, LogLevel.Debug);
        }
        catch (DockerApiException e)
        {
            if (e.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDestroyed),
                        container.LogId],
                    TaskStatus.Success, LogLevel.Debug);
            }
            else
            {
                _logger.LogDeletionFailedWithHttpContext(container.LogId, e.StatusCode, e.ResponseBody);
                return;
            }
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDeletionFailed),
                    container.LogId]);
            return;
        }

        container.Status = ContainerStatus.Destroyed;
    }

    public async Task<Models.Data.Container?> CreateContainerAsync(GZCTF.Models.Internal.ContainerConfig config,
        CancellationToken token = default)
    {
        var imageName = config.Image.Split("/").LastOrDefault()?.Split(":").FirstOrDefault();

        if (string.IsNullOrWhiteSpace(imageName))
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_UnresolvedImageName), config.Image],
                TaskStatus.Failed, LogLevel.Warning);
            return null;
        }

        var attachments = GetNetworkAttachments(config);
        var parameters = GetCreateContainerParameters(config, attachments);
        await EnsureCustomNetworksAsync(config, attachments, token);

        if (_meta.ExposePort && config.PublishPort)
        {
            var exposedPortBindingKey = GetTcpPortKey(config.ExposedPort);
            parameters.ExposedPorts = new Dictionary<string, EmptyStruct> { [exposedPortBindingKey] = new() };
            parameters.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
            {
                // let docker choose a random port, do not use "PublishAllPorts" option
                // reference: https://github.com/moby/moby/blob/master/daemon/libnetwork/portallocator/portallocator.go#L135
                // function: RequestPortsInRange
                // comment:
                //     If portStart and portEnd are 0 it returns
                //     the first free port in the default ephemeral range.
                [exposedPortBindingKey] = [new PortBinding { HostPort = "0" }]
            };
        }

        CreateContainerResponse? containerRes;
        var retry = 0;

    CreateDockerContainer:
        try
        {
            if (retry++ >= 3)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                        parameters.Name], TaskStatus.Failed, LogLevel.Information);
                _logger.LogWarning(
                    "Docker container creation retry limit reached for {ContainerName}. Image={Image}, Network={Network}, ExposedPort={ExposedPort}, CPUCount={CPUCount}, MemoryLimit={MemoryLimit}MiB",
                    parameters.Name, config.Image, parameters.HostConfig.NetworkMode, config.ExposedPort,
                    config.CPUCount, config.MemoryLimit);
                return null;
            }

            containerRes = await _client.Containers.CreateContainerAsync(parameters, token);
        }
        catch (DockerImageNotFoundException ex)
        {
            _logger.LogWarning(ex,
                "Docker image {Image} was not found while creating {ContainerName}; pulling and retrying.",
                config.Image, parameters.Name);
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_PullContainerImage), config.Image],
                TaskStatus.Pending, LogLevel.Information);

            var auth = _meta.AuthConfigs.GetForImage(config.Image) ?? new AuthConfig();

            // pull the image and retry
            await _client.Images.CreateImageAsync(new() { FromImage = config.Image }, auth,
                new Progress<JSONMessage>(msg =>
                {
                    Console.WriteLine($@"{msg.Status}|{msg.Progress}|{msg.Error}");
                }), token);

            goto CreateDockerContainer;
        }
        catch (DockerApiException e)
        {
            _logger.LogWarning(e,
                "Docker API failed while creating {ContainerName}. Status={StatusCode}, Response={ResponseBody}",
                parameters.Name, e.StatusCode, e.ResponseBody);

            if (e.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerExisted),
                        parameters.Name],
                    TaskStatus.Duplicate,
                    LogLevel.Warning);

                // the container already exists, remove it and retry
                try
                {
                    await _client.Containers.RemoveContainerAsync(parameters.Name,
                        new() { Force = true }, token);
                }
                catch (Exception ex)
                {
                    _logger.LogErrorMessage(ex,
                        StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDeletionFailed),
                            parameters.Name]);
                    return null;
                }

                goto CreateDockerContainer;
            }

            _logger.LogCreationFailedWithHttpContext(parameters.Name, e.StatusCode, e.ResponseBody);
            return null;
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                    parameters.Name]);
            return null;
        }

        var container = new Models.Data.Container { ContainerId = containerRes.ID, Image = config.Image };

        retry = 0;

        while (true)
        {
            if (retry++ >= 3)
            {
                _logger.SystemLog(
                    StaticLocalizer[
                        nameof(Resources.Program.ContainerManager_ContainerInstanceStartFailed),
                        container.LogId,
                        config.Image.Split("/").LastOrDefault() ?? ""],
                    TaskStatus.Failed, LogLevel.Warning);

                await DestroyContainerAsync(container, token);
                return null;
            }

            var started = await _client.Containers.StartContainerAsync(container.ContainerId,
                new(), token);

            if (started)
                break;

            await Task.Delay(500, token);
        }

        var info = await _client.Containers.InspectContainerAsync(container.ContainerId, token);

        container.Status = info.State.Dead || info.State.OOMKilled || info.State.Restarting
            ? ContainerStatus.Destroyed
            : info.State.Running
                ? ContainerStatus.Running
                : ContainerStatus.Pending;

        if (container.Status != ContainerStatus.Running)
        {
            _logger.LogWarning(
                "Docker container {ContainerName} for image {Image} started but is not running. State={State}, ExitCode={ExitCode}, Error={Error}",
                parameters.Name, config.Image, info.State.Status, info.State.ExitCode, info.State.Error);

            _logger.SystemLog(
                StaticLocalizer[
                    nameof(Resources.Program.ContainerManager_ContainerInstanceCreationFailedWithError),
                    config.Image.Split("/").LastOrDefault() ?? "", info.State.Error],
                TaskStatus.Failed, LogLevel.Warning);

            await DestroyContainerAsync(container, token);
            return null;
        }

        container.StartedAt = DateTimeOffset.Parse(info.State.StartedAt);
        container.ExpectStopAt = container.StartedAt + TimeSpan.FromHours(2);
        var primaryAttachment = attachments.FirstOrDefault(a => a.IsPrimary) ?? attachments.FirstOrDefault();
        var primaryNetworkName = primaryAttachment?.NetworkName ?? config.NetworkName;
        container.IP = !string.IsNullOrWhiteSpace(primaryNetworkName) &&
                       info.NetworkSettings.Networks.TryGetValue(primaryNetworkName, out var primaryNetwork)
            ? primaryNetwork.IPAddress
            : info.NetworkSettings.Networks.FirstOrDefault().Value.IPAddress;
        container.Port = config.ExposedPort;
        container.IsProxy = !_meta.ExposePort;

        foreach (var attachment in attachments
                     .Where(n => !n.NetworkName.Equals(primaryNetworkName, StringComparison.Ordinal))
                     .DistinctBy(n => n.NetworkName))
        {
            try
            {
                await _client.Networks.ConnectNetworkAsync(attachment.NetworkName,
                    new NetworkConnectParameters
                    {
                        Container = container.ContainerId,
                        EndpointConfig = string.IsNullOrWhiteSpace(attachment.IPAddress)
                            ? null
                            : new EndpointSettings
                            {
                                IPAMConfig = new EndpointIPAMConfig { IPv4Address = attachment.IPAddress }
                            }
                    }, token);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden ||
                                               ex.StatusCode == HttpStatusCode.NotFound ||
                                               ex.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.LogWarning(ex, "Failed to connect container {ContainerId} to network {NetworkName}",
                    container.ContainerId, attachment.NetworkName);
            }
        }

        if (!_meta.ExposePort || !config.PublishPort)
            return container;

        var portString = config.ExposedPort.ToString();
        var exposedPortKey = GetTcpPortKey(config.ExposedPort);
        var bindings = info.NetworkSettings.Ports.TryGetValue(exposedPortKey, out var exactBindings)
            ? exactBindings
            : info.NetworkSettings.Ports
                .Where(kv => kv.Key.Equals(portString, StringComparison.OrdinalIgnoreCase) ||
                             kv.Key.StartsWith($"{portString}/", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .SingleOrDefault();

        if (bindings is not { Count: > 0 })
        {
            _logger.SystemLog(
                StaticLocalizer[
                    nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                    config.Image.Split("/").LastOrDefault() ?? ""],
                TaskStatus.Failed, LogLevel.Warning);

            await DestroyContainerAsync(container, token);
            return null;
        }

        var port = bindings.First().HostPort;

        if (int.TryParse(port, out var numPort))
            container.PublicPort = numPort;
        else
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_PortParsingFailed), port],
                TaskStatus.Failed,
                LogLevel.Warning);

        if (!string.IsNullOrEmpty(_meta.PublicEntry))
            container.PublicIP = _meta.PublicEntry;

        return container;
    }

    public async Task<ContainerPatchApplyResult> ApplyPatchAsync(Models.Data.Container container, Stream archive,
        CancellationToken token = default)
    {
        if (container.Status != ContainerStatus.Running)
            return ContainerPatchApplyResult.Failed(null, "Container is not running");

        var patchDir = $"/tmp/gzctf-awdp-{Guid.NewGuid():N}";

        try
        {
            var initResult = await RunExec(container.ContainerId, ["sh", "-c", $"mkdir -p {patchDir}"],
                TimeSpan.FromSeconds(10), token);

            if (!initResult.Succeeded)
                return initResult;

            await using var tarArchive = await DecompressGzipArchive(archive, token);
            await _client.Containers.ExtractArchiveToContainerAsync(container.ContainerId,
                new CopyToContainerParameters { Path = patchDir, AllowOverwriteDirWithFile = false },
                tarArchive, token);

            var command =
                $"cd {patchDir} && chmod +x update.sh && ./update.sh";
            var result = await RunExec(container.ContainerId, ["sh", "-c", command], PatchApplyTimeout, token);

            _ = await RunExec(container.ContainerId, ["sh", "-c", $"rm -rf {patchDir}"],
                TimeSpan.FromSeconds(10), token);

            return result;
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Invalid AWDP patch archive for container {ContainerId}", container.ContainerId);
            return ContainerPatchApplyResult.Failed(null, "Invalid patch archive");
        }
        catch (DockerApiException ex)
        {
            _logger.LogWarning(ex, "Docker AWDP patch application failed for container {ContainerId}",
                container.ContainerId);
            return ContainerPatchApplyResult.Failed(null, ex.ResponseBody);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AWDP patch application failed for container {ContainerId}", container.ContainerId);
            return ContainerPatchApplyResult.Failed(null, ex.Message);
        }
    }

    static async Task<MemoryStream> DecompressGzipArchive(Stream archive, CancellationToken token)
    {
        archive.Position = 0;
        var output = new MemoryStream();
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
        var buffer = new byte[8192];

        while (true)
        {
            var read = await gzip.ReadAsync(buffer, token);
            if (read == 0)
                break;

            if (output.Length + read > MaxExtractedPatchArchiveSize)
                throw new InvalidDataException("Patch archive is too large after decompression");

            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }

        output.Position = 0;
        return output;
    }

    async Task<ContainerPatchApplyResult> RunExec(string containerId, IList<string> command, TimeSpan timeout,
        CancellationToken token)
    {
        var exec = await _client.Exec.CreateContainerExecAsync(containerId, new ContainerExecCreateParameters
        {
            AttachStderr = true,
            AttachStdout = true,
            Cmd = command
        }, token);

        using var stream = await _client.Exec.StartContainerExecAsync(exec.ID, new ContainerExecStartParameters
        {
            Detach = false
        }, token);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);

        var output = new StringBuilder();

        try
        {
            var buffer = new byte[8192];

            while (true)
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cts.Token);

                if (result.EOF)
                    break;

                if (result.Count > 0 && output.Length < 2048)
                    output.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return ContainerPatchApplyResult.Timeout("Patch execution timed out");
        }

        var inspect = await _client.Exec.InspectContainerExecAsync(exec.ID, token);
        var message = output.ToString().Trim();

        return inspect.ExitCode == 0
            ? ContainerPatchApplyResult.Success(string.IsNullOrWhiteSpace(message) ? "Patch applied" : message)
            : ContainerPatchApplyResult.Failed(inspect.ExitCode,
                string.IsNullOrWhiteSpace(message) ? "Patch command failed" : message);
    }

    private CreateContainerParameters GetCreateContainerParameters(GZCTF.Models.Internal.ContainerConfig config,
        IReadOnlyList<ContainerNetworkAttachment> attachments)
    {
        var primaryAttachment = attachments.FirstOrDefault(a => a.IsPrimary) ?? attachments.FirstOrDefault();
        var primaryNetworkName = primaryAttachment?.NetworkName;
        var hostConfig = new HostConfig
        {
            Memory = config.MemoryLimit * 1024 * 1024,
            NetworkMode = !string.IsNullOrEmpty(primaryNetworkName)
                ? primaryNetworkName
                : _meta.NetworkNames[config.NetworkMode]
        };

        if (config.CPUCount > 0)
        {
            if (_isWindowsDaemon)
                hostConfig.CPUPercent = config.CPUCount * 10;
            else
                hostConfig.NanoCPUs = config.CPUCount * 100_000_000L;
        }

        var env = config.EnvironmentVariables
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToList();

        env.Add($"GZCTF_TEAM_ID={config.TeamId}");
        if (config.Flag is not null)
            env.Add($"GZCTF_FLAG={config.Flag}");

        var createParameters = new CreateContainerParameters
        {
            Image = config.Image,
            NetworkingConfig = !string.IsNullOrWhiteSpace(primaryNetworkName) &&
                               !string.IsNullOrWhiteSpace(primaryAttachment?.IPAddress)
                ? new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [primaryNetworkName] = new()
                        {
                            IPAMConfig = new EndpointIPAMConfig { IPv4Address = primaryAttachment.IPAddress }
                        }
                    }
                }
                : null,
            Labels =
                new Dictionary<string, string>
                {
                    ["TeamId"] = config.TeamId,
                    ["UserId"] = config.UserId.ToString(),
                    ["ChallengeId"] = config.ChallengeId.ToString()
                },
            Name = DockerMetadata.GetName(config),

            // Keep the legacy dynamic-flag environment variable names for challenge image compatibility.
            Env = env,
            HostConfig = hostConfig
        };

        if (!string.IsNullOrWhiteSpace(config.StartCommand))
            createParameters.Cmd = ["sh", "-c", config.StartCommand];

        return createParameters;
    }

    private async Task EnsureCustomNetworksAsync(GZCTF.Models.Internal.ContainerConfig config,
        IReadOnlyList<ContainerNetworkAttachment> attachments, CancellationToken token)
    {
        var customNetworks = attachments.Count > 0
            ? attachments
            : GetNetworkAttachments(config);

        foreach (var attachment in customNetworks.DistinctBy(n => n.NetworkName))
        {
            try
            {
                await _client.Networks.InspectNetworkAsync(attachment.NetworkName, token);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                var parameters = new NetworksCreateParameters
                {
                    Name = attachment.NetworkName,
                    Driver = "bridge",
                    Attachable = true,
                    Internal = attachment.IsInternal,
                    Labels = new Dictionary<string, string> { ["ManagedBy"] = "GZCTF" }
                };

                var subnet = attachment.SubnetCidr;
                if (string.IsNullOrWhiteSpace(subnet))
                    config.NetworkSubnets.TryGetValue(attachment.NetworkName, out subnet);

                if (!string.IsNullOrWhiteSpace(subnet))
                {
                    parameters.IPAM = new IPAM
                    {
                        Config = [new IPAMConfig { Subnet = subnet }]
                    };
                }

                await _client.Networks.CreateNetworkAsync(parameters, token);
            }
        }
    }

    private List<ContainerNetworkAttachment> GetNetworkAttachments(GZCTF.Models.Internal.ContainerConfig config)
    {
        if (config.NetworkAttachments.Count > 0)
        {
            var normalized = config.NetworkAttachments
                .Where(n => !string.IsNullOrWhiteSpace(n.NetworkName))
                .Select(n => new ContainerNetworkAttachment
                {
                    NetworkName = n.NetworkName.Trim(),
                    SubnetCidr = string.IsNullOrWhiteSpace(n.SubnetCidr) ? null : n.SubnetCidr.Trim(),
                    IPAddress = string.IsNullOrWhiteSpace(n.IPAddress) ? null : n.IPAddress.Trim(),
                    IsPrimary = n.IsPrimary,
                    IsInternal = n.IsInternal
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

        var attachments = new List<ContainerNetworkAttachment>();
        if (!string.IsNullOrWhiteSpace(config.NetworkName))
        {
            attachments.Add(new ContainerNetworkAttachment
            {
                NetworkName = config.NetworkName,
                SubnetCidr = config.NetworkSubnets.GetValueOrDefault(config.NetworkName),
                IPAddress = config.IPAddress,
                IsPrimary = true
            });
        }

        foreach (var networkName in config.AdditionalNetworkNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            if (networkName == config.NetworkName)
                continue;

            attachments.Add(new ContainerNetworkAttachment
            {
                NetworkName = networkName,
                SubnetCidr = config.NetworkSubnets.GetValueOrDefault(networkName),
                IsPrimary = false
            });
        }

        return attachments;
    }

    private bool IsWindowsDockerDaemon()
    {
        try
        {
            var info = _client.System.GetSystemInfoAsync().GetAwaiter().GetResult();
            return string.Equals(info.OSType, "windows", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to determine Docker daemon OS type; using Linux-compatible CPU limits.");
            return false;
        }
    }

    static string GetTcpPortKey(int port) => $"{port}/tcp";
}
