using Docker.DotNet;
using Docker.DotNet.Models;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.WebSockets;

namespace GZCTF.Agent.Services;

public class DockerService
{
    private readonly DockerClient _client;
    private readonly DockerConfig _config;
    private readonly ILogger<DockerService> _logger;
    private readonly AgentResourceLock _resourceLock;
    private readonly SemaphoreSlim _registryConfigurationLock = new(1, 1);
    private static readonly TimeSpan FabricCommandTimeout = TimeSpan.FromSeconds(15);

    public DockerService(IOptions<DockerConfig> config, AgentResourceLock resourceLock,
        ILogger<DockerService> logger)
    {
        _config = config.Value;
        _resourceLock = resourceLock;
        _logger = logger;
        _client = new DockerClientConfiguration(new Uri(_config.Uri)).CreateClient();
    }

    public async Task<AgentContainerResponse?> CreateContainerAsync(CreateContainerRequest request, CancellationToken token)
    {
        var containerName = BuildContainerName(request);
        await using var identityLock = await _resourceLock.AcquireAsync($"container:{containerName}", token);
        var fabricManagementNetwork = request.UsePenetrationFabric && request.PublishPort;
        var isolatedHostNetwork = request.UseHostNetworkNone || request.UsePenetrationFabric;
        var gateForTeamLabNetwork = isolatedHostNetwork;
        var attachments = isolatedHostNetwork
            ?
            (fabricManagementNetwork
                ?
                [
                    new ContainerNetworkAttachment
                    {
                        NetworkName = _config.ChallengeNetwork,
                        IsPrimary = true
                    }
                ]
                : [])
                : GetNetworkAttachments(request);
        var primaryAttachment = attachments.FirstOrDefault();
        var primaryNetwork = primaryAttachment?.NetworkName ?? "none";

        foreach (var attachment in attachments)
            await EnsureNetworkAsync(attachment, token);

        var portSpec = $"{request.ExposedPort}/tcp";
        var env = request.EnvironmentVariables
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToList();

        env.Add($"GZCTF_TEAM_ID={request.TeamId}");
        if (request.Flag is not null)
            env.Add($"GZCTF_FLAG={request.Flag}");

        var dnsServers = request.DnsServers
            .Where(server => !string.IsNullOrWhiteSpace(server))
            .Select(server => server.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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
                ["ManagedBy"] = "GZCTF",
                ["GZCTF.RuntimeId"] = request.RuntimeId.ToString(),
                ["GZCTF.Generation"] = Math.Max(1, request.Generation).ToString()
            },
            HostConfig = new HostConfig
            {
                Memory = request.MemoryLimit * 1024L * 1024,
                CPUPercent = request.CPUCount * 10,
                PortBindings = request.PublishPort
                    ? new Dictionary<string, IList<PortBinding>>
                    {
                        [portSpec] = new List<PortBinding> { new() { HostPort = ResolveHostPortBinding(request.PreferredHostPort) } }
                    }
                    : null,
                NetworkMode = isolatedHostNetwork
                    ? fabricManagementNetwork ? primaryNetwork : "none"
                    : primaryNetwork,
                DNS = dnsServers.Length > 0 ? dnsServers : null,
                Binds = request.BindMounts.Select(BuildBindMount).ToList(),
            },
            ExposedPorts = request.PublishPort ? new Dictionary<string, EmptyStruct> { [portSpec] = new() } : null,
            NetworkingConfig = !isolatedHostNetwork &&
                               primaryAttachment is not null &&
                               !string.IsNullOrWhiteSpace(primaryAttachment.IPAddress)
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

        if (request.EnableNetworkAdmin)
        {
            createParams.HostConfig.CapAdd ??= [];
            if (!createParams.HostConfig.CapAdd.Contains("NET_ADMIN"))
                createParams.HostConfig.CapAdd.Add("NET_ADMIN");
        }

        if (request.EnableIpForwarding)
        {
            createParams.HostConfig.Sysctls ??= new Dictionary<string, string>();
            createParams.HostConfig.Sysctls["net.ipv4.ip_forward"] = "1";
        }

        if (gateForTeamLabNetwork)
        {
            ImageInspectResponse image;
            try
            {
                image = await _client.Images.InspectImageAsync(request.Image, token);
            }
            catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"image_not_ready: Docker image {request.Image} is not present.",
                    exception);
            }
            var gated = BuildGatedCommand(image.Config?.Entrypoint, image.Config?.Cmd, request.StartCommand);
            createParams.Entrypoint = gated.Entrypoint;
            createParams.Cmd = gated.Command;
        }
        else if (!string.IsNullOrWhiteSpace(request.StartCommand))
            createParams.Cmd = BuildStartCommand(request.StartCommand);

        try
        {
            var existing = await _client.Containers.InspectContainerAsync(containerName, token);
            var generation = existing.Config.Labels is not null &&
                             existing.Config.Labels.TryGetValue("GZCTF.Generation", out var label)
                ? label
                : null;
            var runtimeId = existing.Config.Labels is not null &&
                            existing.Config.Labels.TryGetValue("GZCTF.RuntimeId", out var runtimeLabel)
                ? runtimeLabel
                : null;
            if (!string.Equals(existing.Config.Image, request.Image, StringComparison.Ordinal) ||
                !string.Equals(generation, Math.Max(1, request.Generation).ToString(), StringComparison.Ordinal) ||
                gateForTeamLabNetwork &&
                !string.Equals(runtimeId, request.RuntimeId.ToString(), StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"runtime_identity_conflict: container {containerName} exists with a different image or generation.");
            if (existing.State?.Running != true)
                await _client.Containers.StartContainerAsync(existing.ID, new ContainerStartParameters(), token);
            return BuildContainerResponse(existing, primaryNetwork, portSpec, request.ExposedPort);
        }
        catch (DockerContainerNotFoundException)
        {
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }

        Docker.DotNet.Models.CreateContainerResponse createResult;
        try
        {
            createResult = await _client.Containers.CreateContainerAsync(createParams, token);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"image_not_ready: Docker image {request.Image} is not present.", ex);
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
            catch (DockerApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or
                                               System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning(ex, "Failed to connect container {ContainerId} to required network {NetworkName}",
                    createResult.ID, attachment.NetworkName);
                await DestroyContainerAsync(createResult.ID, CancellationToken.None);
                return null;
            }
        }

        if (request.RemoveDefaultRoute && !isolatedHostNetwork)
        {
            var routeResult = await ExecuteContainerCommandAsync(createResult.ID,
                ["sh", "-c", "command -v ip >/dev/null 2>&1 || { echo 'missing iproute2/ip command'; exit 127; }; ip route del default 2>/dev/null || true; ip route show"],
                TimeSpan.FromSeconds(10), token);

            if (!routeResult.Succeeded)
            {
                _logger.LogWarning(
                    "Failed to remove default route from container {ContainerId}. Exit={ExitCode}, Message={Message}",
                    createResult.ID, routeResult.ExitCode, routeResult.Message);
                await DestroyContainerAsync(createResult.ID, CancellationToken.None);
                return null;
            }
        }

        var inspect = await _client.Containers.InspectContainerAsync(createResult.ID, token);
        return BuildContainerResponse(inspect, primaryNetwork, portSpec, request.ExposedPort);
    }

    public async Task DestroyContainerAsync(
        string containerId,
        CancellationToken token,
        int? expectedGeneration = null)
    {
        if (expectedGeneration is { } requiredGeneration)
        {
            ContainerInspectResponse existing;
            try
            {
                existing = await _client.Containers.InspectContainerAsync(containerId, token);
            }
            catch (DockerContainerNotFoundException)
            {
                return;
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            if (!MatchesExpectedGeneration(existing.Config.Labels, requiredGeneration, out var legacyGeneration))
                throw new AgentOperationException(
                    "Conflict", "runtime.identity_conflict",
                    "Container generation does not match the requested runtime identity.", false,
                    StatusCodes.Status409Conflict);
            if (legacyGeneration)
                _logger.LogWarning(
                    "Destroying legacy GZCTF container {ContainerId} without a generation label as generation 1",
                    containerId);
        }

        try
        {
            await _client.Containers.StopContainerAsync(containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, token);
        }
        catch (DockerContainerNotFoundException)
        {
            return;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogDebug(ex, "Container {ContainerId} was already stopped", containerId);
        }

        try
        {
            await _client.Containers.RemoveContainerAsync(containerId,
                new ContainerRemoveParameters { Force = true }, token);
        }
        catch (DockerContainerNotFoundException)
        {
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    internal static bool MatchesExpectedGeneration(
        IDictionary<string, string>? labels,
        int requiredGeneration,
        out bool legacyGeneration)
    {
        legacyGeneration = false;
        if (labels is null)
            return false;

        if (labels.TryGetValue("GZCTF.Generation", out var value))
            return int.TryParse(value, out var generation) && generation == requiredGeneration;

        legacyGeneration = requiredGeneration == 1 &&
                           labels.TryGetValue("ManagedBy", out var managedBy) &&
                           string.Equals(managedBy, "GZCTF", StringComparison.Ordinal);
        return legacyGeneration;
    }

    public async Task<AgentCommandResult> ExecuteContainerCommandAsync(string containerId,
        IReadOnlyList<string> command, TimeSpan timeout, CancellationToken token)
    {
        if (command.Count == 0 || command.Any(string.IsNullOrWhiteSpace))
            return AgentCommandResult.Failed(null, "Command is empty");

        try
        {
            var exec = await _client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                AttachStderr = true,
                AttachStdout = true,
                Cmd = command.ToList()
            }, token);

            using var stream = await _client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, token);

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
                return AgentCommandResult.Timeout("Command execution timed out");
            }

            var inspect = await _client.Exec.InspectContainerExecAsync(exec.ID, token);
            var message = output.ToString().Trim();
            return inspect.ExitCode == 0
                ? AgentCommandResult.Success(string.IsNullOrWhiteSpace(message) ? "Command executed" : message)
                : AgentCommandResult.Failed(inspect.ExitCode,
                    string.IsNullOrWhiteSpace(message) ? "Command failed" : message);
        }
        catch (DockerApiException ex)
        {
            _logger.LogWarning(ex, "Docker command execution failed for container {ContainerId}", containerId);
            return AgentCommandResult.Failed(null, ex.ResponseBody);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Docker command execution failed for container {ContainerId}", containerId);
            return AgentCommandResult.Failed(null, ex.Message);
        }
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

    public async Task<AgentFabricResult> CreateFabricNetworkAsync(string networkName, string cidr,
        CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
            return AgentFabricResult.Unsupported("当前 Agent 未运行在 Linux 宿主中，不支持渗透 fabric 网络。");

        var bridgeName = BuildStableFabricName("yyb", networkName);
        return await RunHostFabricCommand(
            [
                "sh",
                "-c",
                $"command -v ip >/dev/null 2>&1 || {{ echo 'missing host ip command'; exit 127; }}; ip link show {ShellQuote(bridgeName)} >/dev/null 2>&1 || ip link add name {ShellQuote(bridgeName)} type bridge; ip link set {ShellQuote(bridgeName)} up"
            ],
            FabricCommandTimeout, token);
    }

    public async Task<AgentFabricResult> AttachFabricInterfaceAsync(string containerId, FabricAttachRequest request,
        CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
            return AgentFabricResult.Unsupported("当前 Agent 未运行在 Linux 宿主中，不支持渗透 fabric 网络。");

        var pid = await GetContainerPidAsync(containerId, token);
        if (pid <= 0)
            return AgentFabricResult.Failed(null, "无法获取容器 PID，不能配置渗透 fabric 网卡。");

        var bridgeName = BuildStableFabricName("yyb", request.NetworkName);
        var hostIf = SanitizeFabricName(request.HostInterfaceName, 15);
        var peerIf = BuildPeerInterfaceName(hostIf);
        var containerIf = SanitizeFabricName(request.ContainerInterfaceName, 15);
        var ipCidr = $"{request.IpAddress}/{request.PrefixLength}";
        var cleanupCommand =
            $"ip link del {ShellQuote(hostIf)} 2>/dev/null || true; nsenter -t {pid} -n ip link del {ShellQuote(containerIf)} 2>/dev/null || true";
        var command = string.Join(' ',
        [
            "set -e;",
            "yy_fabric_ok=0;",
            $"yy_fabric_cleanup() {{ if [ \"$yy_fabric_ok\" != \"1\" ]; then {cleanupCommand}; fi; }};",
            "trap yy_fabric_cleanup 0;",
            "command -v ip >/dev/null 2>&1 || { echo 'missing host ip command'; exit 127; };",
            "command -v nsenter >/dev/null 2>&1 || { echo 'missing nsenter command'; exit 127; };",
            $"ip link show {ShellQuote(bridgeName)} >/dev/null 2>&1 || ip link add name {ShellQuote(bridgeName)} type bridge;",
            $"ip link set {ShellQuote(bridgeName)} up;",
            $"ip link del {ShellQuote(hostIf)} 2>/dev/null || true;",
            $"nsenter -t {pid} -n ip link del {ShellQuote(containerIf)} 2>/dev/null || true;",
            $"ip link add {ShellQuote(hostIf)} type veth peer name {ShellQuote(peerIf)};",
            $"ip link set {ShellQuote(hostIf)} master {ShellQuote(bridgeName)};",
            $"ip link set {ShellQuote(hostIf)} up;",
            $"ip link set {ShellQuote(peerIf)} netns {pid};",
            $"nsenter -t {pid} -n ip link set {ShellQuote(peerIf)} name {ShellQuote(containerIf)};",
            $"nsenter -t {pid} -n ip addr flush dev {ShellQuote(containerIf)};",
            $"nsenter -t {pid} -n ip addr add {ShellQuote(ipCidr)} dev {ShellQuote(containerIf)};",
            $"nsenter -t {pid} -n ip link set {ShellQuote(containerIf)} up;",
            request.RemoveDefaultRoute
                ? $"nsenter -t {pid} -n ip route del default 2>/dev/null || true;"
                : string.Empty,
            $"nsenter -t {pid} -n ip route show;",
            "yy_fabric_ok=1"
        ]);

        return await RunHostFabricCommand(["sh", "-c", command], FabricCommandTimeout, token);
    }

    public async Task<AgentFabricResult> EnableFabricForwardingAsync(string containerId, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
            return AgentFabricResult.Unsupported("当前 Agent 未运行在 Linux 宿主中，不支持渗透 fabric 网络。");

        var pid = await GetContainerPidAsync(containerId, token);
        if (pid <= 0)
            return AgentFabricResult.Failed(null, "无法获取容器 PID，不能开启转发。");

        return await RunHostFabricCommand(
            [
                "sh",
                "-c",
                $"command -v nsenter >/dev/null 2>&1 || {{ echo 'missing nsenter command'; exit 127; }}; nsenter -t {pid} -n sh -c 'echo 1 > /proc/sys/net/ipv4/ip_forward && cat /proc/sys/net/ipv4/ip_forward'"
            ],
            FabricCommandTimeout, token);
    }

    public async Task<AgentFabricResult> ApplyFabricRouteAsync(string containerId, string targetCidr,
        string gatewayIp, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
            return AgentFabricResult.Unsupported("当前 Agent 未运行在 Linux 宿主中，不支持渗透 fabric 网络。");

        var pid = await GetContainerPidAsync(containerId, token);
        if (pid <= 0)
            return AgentFabricResult.Failed(null, "无法获取容器 PID，不能写入路由。");

        return await RunHostFabricCommand(
            [
                "sh",
                "-c",
                $"command -v ip >/dev/null 2>&1 || {{ echo 'missing host ip command'; exit 127; }}; command -v nsenter >/dev/null 2>&1 || {{ echo 'missing nsenter command'; exit 127; }}; nsenter -t {pid} -n ip route replace {ShellQuote(targetCidr)} via {ShellQuote(gatewayIp)} && nsenter -t {pid} -n ip route show {ShellQuote(targetCidr)} | grep -q {ShellQuote(gatewayIp)}"
            ],
            FabricCommandTimeout, token);
    }

    public async Task<AgentFabricResult> ProbeFabricAsync(string containerId, string targetIp, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
            return AgentFabricResult.Unsupported("当前 Agent 未运行在 Linux 宿主中，不支持渗透 fabric 网络。");

        var pid = await GetContainerPidAsync(containerId, token);
        if (pid <= 0)
            return AgentFabricResult.Failed(null, "无法获取容器 PID，不能执行连通探测。");

        return await RunHostFabricCommand(
            [
                "sh",
                "-c",
                $"command -v nsenter >/dev/null 2>&1 || {{ echo 'missing nsenter command'; exit 127; }}; command -v ping >/dev/null 2>&1 || {{ echo 'missing host ping command'; exit 127; }}; nsenter -t {pid} -n ping -c 1 -W 2 {ShellQuote(targetIp)}"
            ],
            TimeSpan.FromSeconds(8), token);
    }

    public async Task<AgentFabricResult> RemoveFabricNetworkAsync(string networkName, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
            return AgentFabricResult.Unsupported("当前 Agent 未运行在 Linux 宿主中，不支持渗透 fabric 网络。");

        var bridgeName = BuildStableFabricName("yyb", networkName);
        return await RunHostFabricCommand(
            ["sh", "-c", $"command -v ip >/dev/null 2>&1 || {{ echo 'missing host ip command'; exit 127; }}; ip link del {ShellQuote(bridgeName)} 2>/dev/null || true"],
            FabricCommandTimeout, token);
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

    internal static IList<string> BuildStartCommand(string command) => ["sh", "-c", command];

    internal static GatedContainerCommand BuildGatedCommand(
        IEnumerable<string>? imageEntrypoint,
        IEnumerable<string>? imageCmd,
        string? startCommand)
    {
        var payload = (imageEntrypoint ?? []).ToList();
        if (string.IsNullOrWhiteSpace(startCommand))
            payload.AddRange(imageCmd ?? []);
        else
            payload.AddRange(["sh", "-c", startCommand]);

        return new GatedContainerCommand(
            [
                "sh",
                "-c",
                "while [ ! -f /tmp/.gzctf-teamlab-network-ready ]; do sleep 0.05; done; exec \"$@\"",
                "teamlab-network-gate"
            ],
            payload);
    }

    public async Task<TeamLabDockerContainerIdentity?> InspectTeamLabContainerAsync(
        string containerId,
        CancellationToken token)
    {
        ContainerInspectResponse inspect;
        try
        {
            inspect = await _client.Containers.InspectContainerAsync(containerId, token);
        }
        catch (DockerContainerNotFoundException)
        {
            return null;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var labels = inspect.Config.Labels;
        var runtimeId = labels is not null && labels.TryGetValue("GZCTF.RuntimeId", out var runtimeValue) &&
                        int.TryParse(runtimeValue, out var parsedRuntime)
            ? parsedRuntime
            : 0;
        var generation = labels is not null && labels.TryGetValue("GZCTF.Generation", out var generationValue) &&
                         int.TryParse(generationValue, out var parsedGeneration)
            ? parsedGeneration
            : 0;
        return new TeamLabDockerContainerIdentity(
            inspect.ID,
            inspect.Name.Trim('/'),
            runtimeId,
            generation,
            inspect.State?.Running == true,
            inspect.State?.Pid ?? 0);
    }

    public async Task RunTeamLabTerminalAsync(
        int runtimeId,
        int generation,
        string containerId,
        WebSocket socket,
        CancellationToken token)
    {
        var identity = await InspectTeamLabContainerAsync(containerId, token);
        if (identity is null || !identity.Running || identity.RuntimeId != runtimeId || identity.Generation != generation)
            throw new AgentOperationException("RemoteAccess", "remote_access.container_identity_mismatch",
                "The requested container does not match the active TeamLab runtime identity.", false);

        var exec = await _client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            AttachStdin = true,
            AttachStdout = true,
            AttachStderr = true,
            Tty = true,
            Cmd = ["/bin/sh"]
        }, token);
        using var stream = await _client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        var input = CopySocketToTerminalAsync(socket, stream, linked.Token);
        var output = CopyTerminalToSocketAsync(socket, stream, linked.Token);
        await Task.WhenAny(input, output);
        linked.Cancel();
        try { await Task.WhenAll(input, output); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
    }

    private static async Task CopySocketToTerminalAsync(WebSocket socket, MultiplexedStream terminal, CancellationToken token)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            await terminal.WriteAsync(buffer, 0, result.Count, token);
            while (!result.EndOfMessage)
            {
                result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                await terminal.WriteAsync(buffer, 0, result.Count, token);
            }
        }
    }

    private static async Task CopyTerminalToSocketAsync(WebSocket socket, MultiplexedStream terminal, CancellationToken token)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await terminal.ReadOutputAsync(buffer, 0, buffer.Length, token);
            if (result.EOF) break;
            if (result.Count > 0)
                await socket.SendAsync(buffer.AsMemory(0, result.Count), WebSocketMessageType.Text, true, token);
        }
    }

    public async Task<IReadOnlyList<RuntimeInventoryResource>> GetManagedRuntimeInventoryAsync(
        CancellationToken token)
    {
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { ["ManagedBy=GZCTF"] = true }
            },
            All = true
        }, token);

        return BuildManagedRuntimeInventory(containers);
    }

    internal static IReadOnlyList<RuntimeInventoryResource> BuildManagedRuntimeInventory(
        IEnumerable<ContainerListResponse> containers) => containers
            .Select(container =>
            {
                if (container.Labels is null ||
                    !container.Labels.TryGetValue("ManagedBy", out var managedBy) ||
                    !managedBy.Equals("GZCTF", StringComparison.Ordinal) ||
                    !container.Labels.TryGetValue("GZCTF.Generation", out var value) ||
                    !int.TryParse(value, out var generation) || generation < 1)
                    return null;

                var stableName = container.Names
                    .Select(name => name.Trim('/'))
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
                if (string.IsNullOrWhiteSpace(container.ID) || string.IsNullOrWhiteSpace(stableName))
                    return null;

                var runtimeId = container.Labels.TryGetValue("GZCTF.RuntimeId", out var runtimeValue) &&
                                int.TryParse(runtimeValue, out var parsedRuntimeId) && parsedRuntimeId > 0
                    ? parsedRuntimeId
                    : (int?)null;

                return new RuntimeInventoryResource(
                    container.ID,
                    stableName,
                    generation,
                    container.State ?? "unknown",
                    container.Image,
                    RuntimeId: runtimeId);
            })
            .OfType<RuntimeInventoryResource>()
            .OrderBy(item => item.StableName, StringComparer.Ordinal)
            .ToArray();

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

    public async Task DeleteImageAsync(string image, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(image))
            throw new InvalidOperationException("Docker image is required.");

        try
        {
            await _client.Images.DeleteImageAsync(image,
                new ImageDeleteParameters { Force = false, NoPrune = false }, token);
        }
        catch (DockerImageNotFoundException)
        {
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    public async Task EnsureRegistryAsync(int port, CancellationToken token)
    {
        const string containerName = "gzctf-internal-registry";
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [containerName] = true }
            }
        }, token);
        var existing = containers.FirstOrDefault(c =>
            c.Names.Any(n => n.Trim('/').Equals(containerName, StringComparison.OrdinalIgnoreCase)));

        if (existing is not null)
        {
            if (RegistryContainerPublishesPort(existing, port))
            {
                if (existing.State?.Equals("running", StringComparison.OrdinalIgnoreCase) != true)
                    await _client.Containers.StartContainerAsync(existing.ID, new ContainerStartParameters(), token);
                return;
            }

            _logger.LogInformation(
                "Recreating internal Docker registry container because published port changed to {Port}.",
                port);
            await DestroyContainerAsync(existing.ID, token);
        }

        try
        {
            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = "registry", Tag = "2" },
                null,
                new Progress<JSONMessage>(),
                token);
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "registry:2 image was not found while preparing internal registry");
            throw;
        }

        var create = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = containerName,
            Image = "registry:2",
            Labels = new Dictionary<string, string>
            {
                ["ManagedBy"] = "GZCTF",
                ["Role"] = "DockerRegistry"
            },
            HostConfig = new HostConfig
            {
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.Always },
                Binds = ["/var/lib/gzctf-registry:/var/lib/registry"],
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["5000/tcp"] = new List<PortBinding> { new() { HostPort = port.ToString() } }
                }
            },
            ExposedPorts = new Dictionary<string, EmptyStruct> { ["5000/tcp"] = new() }
        }, token);

        await _client.Containers.StartContainerAsync(create.ID, new ContainerStartParameters(), token);
    }

    static bool RegistryContainerPublishesPort(ContainerListResponse container, int port)
    {
        var expected = port.ToString();
        return container.Ports.Any(p =>
            p.PrivatePort == 5000 &&
            p.Type.Equals("tcp", StringComparison.OrdinalIgnoreCase) &&
            p.PublicPort.ToString() == expected);
    }

    public async Task ConfigureInsecureRegistryAsync(string registry, CancellationToken token)
    {
        await ConfigureInsecureRegistriesAsync([registry], token);
    }

    public async Task ConfigureInsecureRegistriesAsync(IEnumerable<string> registriesToAdd, CancellationToken token)
    {
        var requestedRegistries = registriesToAdd
            .Select(NormalizeRegistryAddress)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestedRegistries.Length == 0)
            throw new InvalidOperationException("Registry address is required.");

        if (!OperatingSystem.IsLinux())
            throw new NotSupportedException("Automatic Docker daemon registry configuration is only supported on Linux.");

        await _registryConfigurationLock.WaitAsync(token);
        try
        {
            Directory.CreateDirectory("/etc/docker");
            const string daemonPath = "/etc/docker/daemon.json";
            var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(daemonPath) && new FileInfo(daemonPath).Length > 0)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        await File.ReadAllTextAsync(daemonPath, token));
                    if (parsed is not null)
                        foreach (var pair in parsed)
                            data[pair.Key] = pair.Value.Clone();
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Docker daemon.json is invalid; backing up and replacing it");
                }
            }

            var registries = new List<string>();
            if (data.TryGetValue("insecure-registries", out var existing) && existing is JsonElement element &&
                element.ValueKind == JsonValueKind.Array)
            {
                registries.AddRange(element.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))!);
            }

            var changed = false;
            foreach (var registry in requestedRegistries)
            {
                if (registries.Contains(registry, StringComparer.OrdinalIgnoreCase))
                    continue;

                registries.Add(registry);
                changed = true;
            }
            data["insecure-registries"] = registries;

            if (!changed)
                return;

            if (File.Exists(daemonPath))
                File.Copy(daemonPath, $"{daemonPath}.bak.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}", true);
            await File.WriteAllTextAsync(daemonPath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), token);

            await RunHostCommandAsync("systemctl", ["restart", "docker"], TimeSpan.FromSeconds(60), token);
        }
        finally
        {
            _registryConfigurationLock.Release();
        }
    }

    static string NormalizeRegistryAddress(string value)
    {
        var normalized = value.Trim().TrimEnd('/');
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["http://".Length..];
        if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["https://".Length..];
        return normalized;
    }

    static async Task RunHostCommandAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = fileName;
        foreach (var arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        if (process.ExitCode != 0)
        {
            var output = string.Join('\n', new[] { await stdout, await stderr }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(output)
                ? $"{fileName} exited with code {process.ExitCode}"
                : output);
        }
    }

    async Task<AgentFabricResult> RunHostFabricCommand(IReadOnlyList<string> command, TimeSpan timeout,
        CancellationToken token)
    {
        System.Diagnostics.Process? process = null;
        try
        {
            process = new System.Diagnostics.Process();
            process.StartInfo.FileName = command[0];
            foreach (var arg in command.Skip(1))
                process.StartInfo.ArgumentList.Add(arg);
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);
            var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var output = string.Join('\n', new[] { await stdout, await stderr }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

            return process.ExitCode == 0
                ? AgentFabricResult.Success(string.IsNullOrWhiteSpace(output) ? "fabric command executed" : output)
                : AgentFabricResult.Failed(process.ExitCode,
                    string.IsNullOrWhiteSpace(output) ? "fabric command failed" : output);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            try
            {
                if (process is not null && !process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to kill timed out penetration fabric command");
            }

            return AgentFabricResult.Timeout("fabric command timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Penetration fabric host command failed");
            return AgentFabricResult.Failed(null, ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    public async Task<long> GetContainerPidAsync(string containerId, CancellationToken token)
    {
        try
        {
            var inspect = await _client.Containers.InspectContainerAsync(containerId, token);
            return inspect.State.Pid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to inspect PID for container {ContainerId}", containerId);
            return 0;
        }
    }

    static string BuildStableFabricName(string prefix, string value, int maxLength = 15)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        var name = $"{prefix}{hash}";
        return name[..Math.Min(name.Length, maxLength)];
    }

    static string BuildPeerInterfaceName(string hostInterfaceName)
    {
        var peerName = SanitizeFabricName($"p{hostInterfaceName}", 15);
        return peerName.Equals(hostInterfaceName, StringComparison.Ordinal)
            ? BuildStableFabricName("yyr", hostInterfaceName)
            : peerName;
    }

    static string SanitizeFabricName(string value, int maxLength)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = $"yy{Guid.NewGuid():N}";
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    static string ShellQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    private string ResolveHostPortBinding(int? preferredHostPort = null)
    {
        if (preferredHostPort is > 0 and <= ushort.MaxValue)
            return preferredHostPort.Value.ToString();

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
        await using var networkLock = await _resourceLock.AcquireAsync(
            $"docker-network:{attachment.NetworkName}", token);
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

    static AgentContainerResponse BuildContainerResponse(ContainerInspectResponse inspect, string primaryNetwork,
        string portSpec, int exposedPort)
    {
        var network = !primaryNetwork.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                      !string.IsNullOrWhiteSpace(primaryNetwork) &&
                      inspect.NetworkSettings.Networks.TryGetValue(primaryNetwork, out var selected)
            ? selected
            : inspect.NetworkSettings.Networks.FirstOrDefault().Value;
        var portBinding = inspect.NetworkSettings.Ports is not null &&
                          inspect.NetworkSettings.Ports.TryGetValue(portSpec, out var bindings)
            ? bindings?.FirstOrDefault()
            : null;
        return new AgentContainerResponse
        {
            ContainerId = inspect.ID,
            ContainerName = inspect.Name.Trim('/'),
            IP = network?.IPAMConfig?.IPv4Address ?? network?.IPAddress ?? string.Empty,
            Port = exposedPort,
            PublicPort = int.TryParse(portBinding?.HostPort, out var port) ? port : 0
        };
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

    private static string BuildBindMount(ContainerBindMount mount)
    {
        var source = Path.GetFullPath(mount.Source);
        var destination = Path.GetFullPath(mount.Destination);
        var sourceAllowed = source.StartsWith("/run/gzctf-sensor/", StringComparison.Ordinal) ||
                            source.StartsWith("/opt/gzctf/endpoint-sensor/", StringComparison.Ordinal);
        var destinationAllowed = destination.StartsWith("/run/gzctf/", StringComparison.Ordinal) ||
                                 destination.StartsWith("/opt/gzctf/", StringComparison.Ordinal);
        if (!sourceAllowed || !destinationAllowed || source.Contains(':') || destination.Contains(':'))
            throw new InvalidOperationException("Container bind mount is outside the managed sensor paths.");
        return $"{source}:{destination}:{(mount.ReadOnly ? "ro" : "rw")}";
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

internal sealed record GatedContainerCommand(
    IList<string> Entrypoint,
    IList<string> Command);

public sealed record TeamLabDockerContainerIdentity(
    string ContainerId,
    string ContainerName,
    int RuntimeId,
    int Generation,
    bool Running,
    long Pid);
