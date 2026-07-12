using System.Security.Cryptography;
using System.Text;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabNodeExecutor(
    AppDbContext context,
    AgentClient agent,
    DockerImageRegistryService dockerRegistry,
    ImageDistributionService imageDistribution,
    IOptions<TeamLabNetworkConfig> options,
    ILogger<AgentTeamLabNodeExecutor> logger) : ITeamLabNodeExecutor
{
    private readonly TeamLabNetworkConfig _config = options.Value;

    public async Task<TeamLabNodeResult> ApplyShardAsync(
        Guid workerNodeId,
        TeamLabNodeShardApplyRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var network in request.Networks)
        {
            var bridge = await agent.CreateTeamLabBridgeAsync(workerNodeId,
                new TeamLabBridgeRequest(request.RuntimeId, network.BridgeName, network.Cidr, _config.DryRun),
                cancellationToken);
            var bridgeResult = RequireMutation(bridge, $"Failed to create bridge {network.BridgeName}.");
            if (!bridgeResult.Success) return bridgeResult;
        }

        var router = await agent.CreateTeamLabRouterAsync(workerNodeId,
            new TeamLabRouterRequest(
                request.RuntimeId,
                request.RouterNamespace,
                request.Networks.Select(network => new TeamLabRouterInterfaceRequest(
                    network.BridgeName, $"{network.GatewayIp}/{Prefix(network.Cidr)}")).ToArray(),
                [],
                _config.DryRun),
            cancellationToken);
        var routerResult = RequireMutation(router, $"Failed to create router {request.RouterNamespace}.");
        if (!routerResult.Success) return routerResult;

        foreach (var (network, index) in request.Networks.Select((value, index) => (value, index)))
        {
            request.RecordsByNetwork.TryGetValue(network.Key, out var records);
            records ??= [];
            var serviceName = LinuxName($"tld{request.RuntimeId}-{network.Key}");
            var dhcpDns = await agent.ConfigureTeamLabDhcpDnsAsync(workerNodeId,
                new TeamLabDhcpDnsRequest(
                    request.RuntimeId,
                    serviceName,
                    request.RouterNamespace,
                    network.BridgeName,
                    LinuxName($"{request.RouterNamespace}d{index}"),
                    network.GatewayIp,
                    network.Cidr,
                    $"teamlab{request.RuntimeId}.local",
                    records.Select(item => new TeamLabDhcpLeaseRequest(item.MacAddress, item.IpAddress, Hostname(item.Hostname))).ToArray(),
                    records.Select(item => new TeamLabDnsRecordRequest(Hostname(item.Hostname), item.IpAddress)).ToArray(),
                    _config.DryRun),
                cancellationToken);
            var dnsResult = RequireMutation(dhcpDns, $"Failed to configure DHCP/DNS for {network.Name}.");
            if (!dnsResult.Success) return dnsResult;
        }
        return TeamLabNodeResult.Ok("Shard network applied.");
    }

    public async Task<TeamLabNodeResult> ApplyRoutesAsync(
        Guid workerNodeId,
        TeamLabNodeRouteApplyRequest request,
        CancellationToken cancellationToken)
    {
        var response = await agent.ApplyTeamLabFabricAsync(workerNodeId,
            new TeamLabFabricApplyRequest(
                request.RuntimeId,
                request.RouteVersion,
                request.FabricIp,
                request.RouterNamespace,
                request.NamespaceHostAddressCidr,
                request.NamespacePeerAddressCidr,
                request.LocalRoutes.Select(ToAgentRoute).ToArray(),
                request.RemoteRoutes.Select(ToAgentRoute).ToArray(),
                request.ForwardPolicies.Select(policy => new TeamLabForwardPolicyRequest(
                    policy.SourceCidr,
                    policy.DestinationCidr,
                    policy.Allow)).ToArray(),
                _config.DryRun),
            cancellationToken);
        return RequireMutation(response, "Failed to apply TeamLab Fabric routes.");
    }

    public async Task<TeamLabNodeAssetCreateResult> CreateAssetAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await context.ImageTemplates.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.ImageTemplateId, cancellationToken);
        if (template is null || template.Status != ImageStatus.Ready)
            return TeamLabNodeAssetCreateResult.Failed($"Image template {request.ImageTemplateId} is not ready.");

        try
        {
            return request.Kind == TeamLabAssetKind.Docker
                ? await CreateContainerAsync(workerNodeId, request, template, cancellationToken)
                : await CreateVmAsync(workerNodeId, request, template, cancellationToken);
        }
        catch (Exception exception) when (exception is AgentClientException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(exception,
                "TeamLab asset creation failed: runtime={RuntimeId}, generation={Generation}, asset={AssetKey}, node={NodeId}",
                request.RuntimeId, request.Generation, request.AssetKey, workerNodeId);
            return TeamLabNodeAssetCreateResult.Failed(exception.Message);
        }
    }

    public async Task<TeamLabNodeResult> DestroyAssetAsync(
        Guid workerNodeId,
        TeamLabAssetKind kind,
        string resourceId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (kind == TeamLabAssetKind.Docker)
                await agent.DestroyContainerAsync(workerNodeId, resourceId, cancellationToken);
            else
                await agent.DestroyVmAsync(workerNodeId, resourceId, cancellationToken);
            return TeamLabNodeResult.Ok("Asset destroyed.");
        }
        catch (Exception exception) when (exception is AgentClientException or HttpRequestException or TaskCanceledException)
        {
            return TeamLabNodeResult.Failed(exception.Message);
        }
    }

    public async Task<TeamLabNodeResult> CleanupShardAsync(
        Guid workerNodeId,
        TeamLabNodeCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var containerId in request.ContainerIds.Distinct(StringComparer.Ordinal))
        {
            var result = await DestroyAssetAsync(workerNodeId, TeamLabAssetKind.Docker, containerId, cancellationToken);
            if (!result.Success) errors.Add(result.Message);
        }
        foreach (var vmName in request.VmNames.Distinct(StringComparer.Ordinal))
        {
            var result = await DestroyAssetAsync(workerNodeId, TeamLabAssetKind.Vm, vmName, cancellationToken);
            if (!result.Success) errors.Add(result.Message);
        }
        var cleanup = await agent.CleanupTeamLabAsync(workerNodeId,
            new GZCTF.Services.Fleet.TeamLabCleanupRequest(request.RuntimeId, request.ResourceNames.Distinct(StringComparer.Ordinal).ToArray(), _config.DryRun),
            cancellationToken);
        var cleanupResult = RequireMutation(cleanup, "TeamLab shard cleanup failed.");
        if (!cleanupResult.Success) errors.Add(cleanupResult.Message);
        return errors.Count == 0 ? TeamLabNodeResult.Ok("Shard cleaned.") : TeamLabNodeResult.Failed(string.Join("; ", errors));
    }

    public async Task<TeamLabNodeResult> ProbeAsync(
        Guid workerNodeId,
        TeamLabNodeProbeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await agent.ProbeTeamLabAsync(workerNodeId,
            new GZCTF.Services.Fleet.TeamLabProbeRequest(request.RuntimeId, request.RouterNamespace, request.TargetIp, _config.DryRun),
            cancellationToken);
        return RequireMutation(response, $"Probe to {request.TargetIp} failed.");
    }

    public async Task<TeamLabNodeResult> ConfigureAccessAsync(
        Guid workerNodeId,
        TeamLabNodeAccessApplyRequest request,
        CancellationToken cancellationToken)
    {
        var response = await agent.ConfigureTeamLabWireGuardAsync(workerNodeId,
            new TeamLabWireGuardRequest(
                request.RuntimeId,
                request.RouterNamespace,
                request.InterfaceName,
                request.ListenPort,
                request.ServerAddressCidr,
                request.ServerPrivateKey,
                request.ClientPublicKey,
                request.ClientAddress,
                request.ClientAllowedIps,
                request.PlayerAllowedCidrs.ToArray(),
                request.PlayerBlockedCidrs.ToArray(),
                _config.DryRun),
            cancellationToken);
        return RequireMutation(response, "Failed to configure TeamLab WireGuard access.");
    }

    public async Task<TeamLabNodeResult> StartFlowAsync(
        Guid workerNodeId,
        int runtimeId,
        int shardId,
        int networkId,
        string networkKey,
        string interfaceName,
        CancellationToken cancellationToken)
    {
        var response = await agent.StartTeamLabFlowMetadataAsync(workerNodeId,
            new TeamLabFlowStartRequest(runtimeId, shardId, networkId, networkKey, interfaceName, _config.DryRun), cancellationToken);
        return RequireFlowMutation(response, "Failed to start traffic metadata collection.");
    }

    public async Task<TeamLabNodeResult> StopFlowAsync(
        Guid workerNodeId,
        int runtimeId,
        string networkKey,
        CancellationToken cancellationToken)
    {
        var response = await agent.StopTeamLabFlowMetadataAsync(workerNodeId,
            new TeamLabFlowStopRequest(runtimeId, networkKey, _config.DryRun), cancellationToken);
        return RequireFlowMutation(response, "Failed to stop traffic metadata collection.");
    }

    public async Task<TeamLabNodeFlowResult> GetFlowSnapshotAsync(
        Guid workerNodeId,
        int runtimeId,
        string networkKey,
        long afterCursor,
        CancellationToken cancellationToken)
    {
        var response = await agent.GetTeamLabFlowMetadataSnapshotAsync(workerNodeId,
            new TeamLabFlowSnapshotRequest(runtimeId, networkKey, afterCursor, _config.DryRun), cancellationToken);
        if (response is not { Success: true } || response.DryRun)
            return new TeamLabNodeFlowResult(
                false,
                response?.Message ?? "Traffic metadata snapshot failed.",
                afterCursor,
                []);
        return new TeamLabNodeFlowResult(
            true,
            response.Message,
            response.NextCursor,
            response.Samples.Select(item => new TeamLabNodeFlowSample(
                item.Cursor,
                item.CapturedAt,
                item.SourceIp,
                item.SourcePort,
                item.DestinationIp,
                item.DestinationPort,
                item.Protocol,
                item.Bytes)).ToArray());
    }

    public async Task<TeamLabNodeCaptureResult> StartCaptureAsync(
        Guid workerNodeId,
        TeamLabNodeCaptureStartRequest request,
        CancellationToken cancellationToken)
    {
        var response = await agent.StartTeamLabCaptureAsync(workerNodeId,
            new TeamLabCaptureStartRequest(request.RuntimeId, request.JobId, request.Scope, request.InterfaceName,
                request.MaxSeconds, request.MaxBytes, _config.DryRun), cancellationToken);
        return ToCaptureResult(response, "Failed to start traffic capture.");
    }

    public async Task<TeamLabNodeCaptureResult> StopCaptureAsync(
        Guid workerNodeId,
        int runtimeId,
        int jobId,
        CancellationToken cancellationToken)
    {
        var response = await agent.StopTeamLabCaptureAsync(workerNodeId,
            new TeamLabCaptureStopRequest(runtimeId, jobId, _config.DryRun), cancellationToken);
        return ToCaptureResult(response, "Failed to stop traffic capture.");
    }

    public async Task<TeamLabNodeCaptureResult> GetCaptureStatusAsync(
        Guid workerNodeId,
        int runtimeId,
        int jobId,
        CancellationToken cancellationToken)
    {
        var response = await agent.GetTeamLabCaptureStatusAsync(workerNodeId,
            new TeamLabCaptureStatusRequest(runtimeId, jobId, _config.DryRun), cancellationToken);
        return ToCaptureResult(response, "Failed to read traffic capture status.");
    }

    public async Task<TeamLabNodeCaptureDownload> DownloadCaptureAsync(
        Guid workerNodeId,
        int runtimeId,
        int jobId,
        CancellationToken cancellationToken)
    {
        var result = await agent.DownloadTeamLabCaptureAsync(workerNodeId, runtimeId, jobId, cancellationToken);
        return result is not { Success: true }
            ? new TeamLabNodeCaptureDownload(false, result?.Message ?? "Failed to download traffic capture.", null,
                string.Empty, "application/octet-stream", null, null)
            : new TeamLabNodeCaptureDownload(true, result.Message, result.Stream, result.FileName, result.ContentType,
                result.Length, result.Owner);
    }

    private async Task<TeamLabNodeAssetCreateResult> CreateContainerAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        ImageTemplate template,
        CancellationToken cancellationToken)
    {
        if (template.ImageType != ImageType.Docker)
            return TeamLabNodeAssetCreateResult.Failed($"Image template {template.Id} is not a Docker template.");
        var image = DockerImageReference.ResolvePullTarget(template.Name, template.RegistryUrl).FullImage;
        image = await dockerRegistry.ResolveImageReferenceAsync(image, cancellationToken);
        await imageDistribution.EnsureDockerImageOnNodeAsync(image, workerNodeId, cancellationToken);
        var environment = request.Environment.Concat(request.Secrets)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var config = new ContainerConfig
        {
            Image = image,
            TeamId = $"teamlab-{request.RuntimeId}",
            ChallengeId = StableId(request.AssetKey),
            UserId = Guid.Empty,
            ExposedPort = request.ExposePort ?? 80,
            Flag = request.Secrets.GetValueOrDefault("FLAG") ?? request.Secrets.GetValueOrDefault("GZCTF_FLAG"),
            CPUCount = request.CpuUnits,
            MemoryLimit = request.MemoryMiB,
            StorageLimit = request.StorageMiB,
            NetworkMode = NetworkMode.Custom,
            PublishPort = false,
            BypassPublicProxy = true,
            UsePenetrationFabric = false,
            UseHostNetworkNone = true,
            EnableNetworkAdmin = request.RoutingEnabled,
            EnableIpForwarding = request.RoutingEnabled,
            PreferredNodeId = workerNodeId,
            DnsServers = request.Interfaces.SelectMany(item => item.DnsServers).Distinct(StringComparer.Ordinal).ToList(),
            EnvironmentVariables = environment
        };
        var container = await agent.CreateContainerOrThrowAsync(workerNodeId, config, cancellationToken);
        foreach (var iface in request.Interfaces)
        {
            var attach = await agent.AttachTeamLabContainerAsync(workerNodeId,
                new TeamLabContainerAttachRequest(
                    request.RuntimeId,
                    container.ContainerId,
                    iface.BridgeName,
                    HostInterfaceName(request.RuntimeId, request.AssetKey, iface.Key),
                    iface.Key,
                    $"{iface.IpAddress}/{iface.PrefixLength}",
                    iface.MacAddress,
                    false,
                    Gateway(iface.IpAddress, iface.PrefixLength),
                    iface.Routes.ToArray(),
                    iface.DnsServers.ToArray(),
                    _config.DryRun),
                cancellationToken);
            var attachResult = RequireMutation(attach, $"Failed to attach container interface {iface.Key}.");
            if (!attachResult.Success)
            {
                await agent.DestroyContainerAsync(workerNodeId, container.ContainerId, cancellationToken);
                return TeamLabNodeAssetCreateResult.Failed(attachResult.Message);
            }
        }
        return TeamLabNodeAssetCreateResult.Created(container.ContainerId);
    }

    private async Task<TeamLabNodeAssetCreateResult> CreateVmAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        ImageTemplate template,
        CancellationToken cancellationToken)
    {
        if (template.ImageType == ImageType.Docker)
            return TeamLabNodeAssetCreateResult.Failed($"Image template {template.Id} is not a VM template.");
        var imageReady = await imageDistribution.EnsureVmTemplateOnNodeAsync(template.Id, workerNodeId, cancellationToken);
        if (!imageReady.Success) return TeamLabNodeAssetCreateResult.Failed(imageReady.Message);
        var vmName = LinuxName($"tl{request.RuntimeId}-{request.AssetKey}");
        var interfaces = request.Interfaces.Select(iface => new AgentVmNetworkInterfaceRequest
        {
            BridgeName = iface.BridgeName,
            MacAddress = iface.MacAddress,
            Model = template.OSType == OSType.Windows ? "e1000e" : "virtio",
            InterfaceName = iface.Key,
            IpAddress = iface.IpAddress,
            PrefixLength = iface.PrefixLength,
            Gateway = iface.Primary ? Gateway(iface.IpAddress, iface.PrefixLength) : null,
            DnsServers = iface.DnsServers.ToList(),
            Routes = iface.Routes.Select(route => $"{route} via {Gateway(iface.IpAddress, iface.PrefixLength)}").ToList(),
            IsPrimary = iface.Primary
        }).ToList();
        var vmRequest = new AgentCreateVmRequest
        {
            TemplateId = template.Id,
            TemplatePath = template.LocalFilePath ?? template.Name,
            ImageEnsured = true,
            VmName = vmName,
            Memory = request.MemoryMiB,
            Cpu = Math.Max(1, request.CpuUnits),
            Flag = request.Secrets.GetValueOrDefault("FLAG") ?? request.Secrets.GetValueOrDefault("GZCTF_FLAG"),
            Interfaces = interfaces,
            CloudInit = BuildCloudInit(request, template.OSType, vmName, interfaces)
        };
        var vm = await agent.CreateVmAsync(workerNodeId, vmRequest, cancellationToken);
        if (vm is null || string.IsNullOrWhiteSpace(vm.VmName))
            return TeamLabNodeAssetCreateResult.Failed($"Failed to create VM {request.Name}.");
        var primaryIp = request.Interfaces.First(item => item.Primary).IpAddress;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var status = await agent.GetVmIpAsync(workerNodeId, vm.VmName, interfaces, cancellationToken);
            if (status is not null && string.Equals(status.IpAddress, primaryIp, StringComparison.Ordinal) &&
                (string.Equals(status.Status, "Ready", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(status.Status, "Running", StringComparison.OrdinalIgnoreCase)))
                return TeamLabNodeAssetCreateResult.Created(vm.VmName);
            if (attempt < 23) await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        await agent.DestroyVmAsync(workerNodeId, vm.VmName, cancellationToken);
        return TeamLabNodeAssetCreateResult.Failed($"VM {request.Name} did not reach IP {primaryIp}.");
    }

    private static AgentVmInitConfig BuildCloudInit(
        TeamLabNodeAssetCreateRequest request,
        OSType osType,
        string vmName,
        IReadOnlyList<AgentVmNetworkInterfaceRequest> interfaces)
    {
        if (osType != OSType.Linux)
            return new AgentVmInitConfig { Enabled = false, OsType = osType, Hostname = vmName, InstanceId = $"teamlab-{request.RuntimeId}-{request.AssetKey}" };
        var values = request.Environment.Concat(request.Secrets)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var userData = new StringBuilder("#cloud-config\nwrite_files:\n  - path: /opt/gzctf/runtime/env\n    owner: root:root\n    permissions: '0600'\n    content: |\n");
        userData.AppendLine($"      GZCTF_RUNTIME_ID='{request.RuntimeId}'");
        userData.AppendLine($"      GZCTF_TOPOLOGY_KEY='{ShellSingleQuote(request.AssetKey)}'");
        foreach (var pair in values.OrderBy(item => item.Key, StringComparer.Ordinal))
            userData.AppendLine($"      {pair.Key}='{ShellSingleQuote(pair.Value)}'");
        userData.AppendLine("runcmd:");
        userData.AppendLine("  - [ bash, -lc, 'test ! -x /opt/gzctf/bin/firstboot || /opt/gzctf/bin/firstboot' ]");
        return new AgentVmInitConfig
        {
            Enabled = true,
            OsType = osType,
            Hostname = vmName,
            InstanceId = $"teamlab-{request.RuntimeId}-{request.AssetKey}",
            MetaData = $"instance-id: teamlab-{request.RuntimeId}-{request.AssetKey}\nlocal-hostname: {vmName}\n",
            UserData = userData.ToString(),
            NetworkConfig = BuildNetworkConfig(interfaces),
            SensitiveKeys = request.Secrets.Keys.Append("user-data").ToList()
        };
    }

    private static string BuildNetworkConfig(IReadOnlyList<AgentVmNetworkInterfaceRequest> interfaces)
    {
        var output = new StringBuilder("version: 2\nethernets:\n");
        foreach (var iface in interfaces)
        {
            output.AppendLine($"  {iface.InterfaceName}:");
            output.AppendLine("    match:");
            output.AppendLine($"      macaddress: \"{iface.MacAddress}\"");
            output.AppendLine($"    set-name: {iface.InterfaceName}");
            output.AppendLine("    dhcp4: false");
            output.AppendLine($"    addresses: [{iface.IpAddress}/{iface.PrefixLength}]");
            if (!string.IsNullOrWhiteSpace(iface.Gateway)) output.AppendLine($"    gateway4: {iface.Gateway}");
            if (iface.DnsServers.Count > 0)
            {
                output.AppendLine("    nameservers:");
                output.AppendLine($"      addresses: [{string.Join(", ", iface.DnsServers)}]");
            }
            if (iface.Routes.Count > 0)
            {
                output.AppendLine("    routes:");
                foreach (var route in iface.Routes)
                {
                    var parts = route.Split(" via ", StringSplitOptions.TrimEntries);
                    output.AppendLine($"      - to: {parts[0]}");
                    output.AppendLine($"        via: {parts[1]}");
                }
            }
        }
        return output.ToString();
    }

    private static TeamLabStaticRouteRequest ToAgentRoute(TeamLabNodeRouteIntent route) =>
        new(route.TargetCidr, route.GatewayIp, route.SourceIp);

    private TeamLabNodeResult RequireMutation(TeamLabDryRunResponse? response, string fallback) =>
        response is not { Success: true }
            ? TeamLabNodeResult.Failed(response?.Message ?? fallback)
            : response.DryRun
                ? TeamLabNodeResult.Failed(response.Message)
                : TeamLabNodeResult.Ok(response.Message);

    private TeamLabNodeResult RequireFlowMutation(TeamLabFlowResponse? response, string fallback) =>
        response is not { Success: true }
            ? TeamLabNodeResult.Failed(response?.Message ?? fallback)
            : response.DryRun
                ? TeamLabNodeResult.Failed(response.Message)
                : TeamLabNodeResult.Ok(response.Message);

    private TeamLabNodeCaptureResult ToCaptureResult(TeamLabCaptureResponse? response, string fallback) =>
        response is not { Success: true } || response.DryRun
            ? new TeamLabNodeCaptureResult(
                false,
                response?.Message ?? fallback,
                response?.FilePath,
                response?.CapturedBytes ?? 0,
                false)
            : new TeamLabNodeCaptureResult(
                true,
                response.Message,
                response.FilePath,
                response.CapturedBytes,
                response.Running);

    private static int Prefix(string cidr) => int.Parse(cidr.Split('/')[1]);
    private static string LinuxName(string value) => value.Length <= 15 ? value : value[..15];
    private static string Hostname(string value) => new(value.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
    private static string ShellSingleQuote(string value) => value.Replace("'", "'\"'\"'");
    private static int StableId(string value) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0) & int.MaxValue;
    private static string HostInterfaceName(int runtimeId, string assetKey, string ifaceKey) =>
        LinuxName($"tl{runtimeId}v{StableId($"{assetKey}:{ifaceKey}"):x}");

    private static string Gateway(string ipAddress, int prefix)
    {
        var bytes = System.Net.IPAddress.Parse(ipAddress).GetAddressBytes();
        var raw = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var mask = uint.MaxValue << (32 - prefix);
        var gateway = (raw & mask) + 1;
        return new System.Net.IPAddress(new[] { (byte)(gateway >> 24), (byte)(gateway >> 16), (byte)(gateway >> 8), (byte)gateway }).ToString();
    }
}
