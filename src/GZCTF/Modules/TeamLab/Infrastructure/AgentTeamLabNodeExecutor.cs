using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.GuestControl.Contracts;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabNodeExecutor(
    IServiceScopeFactory scopeFactory,
    AgentClient agent,
    DockerImageRegistryService dockerRegistry,
    ImageDistributionService imageDistribution,
    RuntimeSignalService runtimeSignals,
    NodeDispatchLimiter dispatchLimiter,
    IOptions<TeamLabNetworkConfig> options,
    ILogger<AgentTeamLabNodeExecutor> logger) : ITeamLabNodeExecutor
{
    private readonly TeamLabNetworkConfig _config = options.Value;
    private readonly ConcurrentDictionary<Guid, Task<AgentExecutionLimits?>> _dispatchLimits = new();

    public async Task<TeamLabNodeRuntimeInventory> GetRuntimeInventoryAsync(
        Guid workerNodeId,
        CancellationToken cancellationToken)
    {
        var inventory = await DispatchAsync(
            workerNodeId,
            NodeDispatchCategory.Probe,
            operationToken => agent.GetRuntimeInventoryAsync(workerNodeId, operationToken),
            cancellationToken);
        static TeamLabNodeInventoryResource Map(GZCTF.Modules.Runtime.Contracts.AgentRuntimeInventoryResource item) =>
            new(item.NativeId, item.StableName, item.Generation, item.State, item.RuntimeId);
        // A missing response is a protocol failure and must stay distinguishable from a node that
        // genuinely holds nothing; reporting it as an empty inventory would let deployment
        // verification blame the assets instead of the unreachable node.
        if (inventory is null)
            throw new TeamLabRuntimeExecutionException(
                $"WorkerNode {workerNodeId} returned no runtime inventory.");
        return new TeamLabNodeRuntimeInventory(
            (inventory.Containers ?? []).Select(Map).ToArray(),
            (inventory.Vms ?? []).Select(Map).ToArray(),
            (inventory.TeamLabResources ?? []).Select(Map).ToArray(),
            inventory.ObservedAt);
    }

    public async Task<TeamLabNodeInfrastructureResult> ApplyInfrastructureAsync(
        Guid workerNodeId,
        TeamLabNodeInfrastructureApplyRequest request,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.TeamLabNetwork,
            operationToken => agent.ApplyTeamLabInfrastructureAsync(workerNodeId,
            new TeamLabInfrastructureApplyRequest(
                request.RuntimeId,
                request.Generation,
                request.RouteVersion,
                request.RouterNamespace,
                request.Switches.Select(item => new TeamLabManagedSwitchIntent(
                    item.Network.Key,
                    item.Network.Name,
                    item.Network.Cidr,
                    item.Network.GatewayIp,
                    item.Network.BridgeName,
                    item.DhcpDnsServiceName,
                    item.Records.Select(record => new TeamLabDhcpLeaseRequest(
                        record.MacAddress,
                        record.IpAddress,
                        Hostname(record.Hostname),
                        record.IsPrimary)).ToArray(),
                    (item.DnsRecords ?? item.Records).Select(record => new TeamLabDnsRecordRequest(
                        Hostname(record.Hostname),
                        record.IpAddress)).ToArray())).ToArray(),
                request.Routers.Select(item => new TeamLabManagedRouterFragmentIntent(
                    item.Key,
                    item.NetworkKeys.ToArray())).ToArray(),
                new TeamLabFabricUplinkIntent(
                    request.Fabric.FabricIp,
                    request.Fabric.HubAddressCidr,
                    request.Fabric.NodeAddressCidr,
                    request.Fabric.HostInterfaceName,
                    request.Fabric.NamespaceInterfaceName,
                    request.Fabric.LocalRoutes.Select(ToAgentRoute).ToArray(),
                    request.Fabric.RemoteRoutes.Select(ToAgentRoute).ToArray()),
                request.ForwardPolicies.Select(policy => new TeamLabForwardPolicyRequest(
                    policy.SourceCidr,
                    policy.DestinationCidr,
                    policy.Allow)).ToArray(),
                request.ObservationPoints.Select(item => new TeamLabObservationPointIntent(
                    item.PublicId,
                    item.TopologyKey,
                    (byte)item.Kind,
                    item.InterfaceToken)).ToArray(),
                _config.DryRun),
            operationToken), cancellationToken);
        if (response is not { Success: true } || response.DryRun ||
            string.IsNullOrWhiteSpace(response.DesiredStateDigest))
            return TeamLabNodeInfrastructureResult.Failed(
                response?.Message ?? "Failed to apply TeamLab infrastructure desired state.");
        return TeamLabNodeInfrastructureResult.Applied(
            response.DesiredStateDigest,
            response.AlreadyApplied,
            response.Resources.Select(item => new TeamLabNodeInfrastructureResourceFact(
                item.Kind,
                item.Key,
                item.NativeIdentity,
                item.Status)).ToArray());
    }

    public Task<TeamLabNodeAssetCreateResult> CreateAssetAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken) =>
        DispatchAsync(
            workerNodeId,
            request.Kind == TeamLabAssetKind.Docker
                ? NodeDispatchCategory.DockerCreate
                : NodeDispatchCategory.VmCreate,
            operationToken => CreateAssetCoreAsync(workerNodeId, request, operationToken),
            cancellationToken);

    private async Task<TeamLabNodeAssetCreateResult> CreateAssetCoreAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = await db.ImageTemplates.AsNoTracking()
            .Include(item => item.PreparedArtifact)
            .Include(item => item.CapabilityCertifications)
            .SingleOrDefaultAsync(item => item.Id == request.ImageTemplateId, cancellationToken);
        if (template is null || template.Status != ImageStatus.Ready)
            return TeamLabNodeAssetCreateResult.Failed($"Image template {request.ImageTemplateId} is not ready.");

        TeamLabEndpointSensorResponse? sensor = null;
        try
        {
            sensor = request.Kind == TeamLabAssetKind.Docker
                ? await RegisterEndpointSensorAsync(workerNodeId, request, cancellationToken)
                : null;
            if (request.EndpointObservation == TeamLabEndpointObservationMode.Required && sensor is not { Success: true })
                return TeamLabNodeAssetCreateResult.Failed(
                    sensor?.Message ?? "Required endpoint sensor channel could not be registered.");
            var result = request.Kind == TeamLabAssetKind.Docker
                ? await CreateContainerAsync(workerNodeId, request, template, sensor?.ChannelEndpoint, cancellationToken)
                : await CreateVmAsync(db, workerNodeId, request, template, cancellationToken);
            if (!result.Success && sensor is { Success: true })
                await RemoveEndpointSensorAsync(workerNodeId, request, cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is AgentClientException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(exception,
                "TeamLab 资源创建失败: runtime={RuntimeId}, generation={Generation}, asset={AssetKey}, node={NodeId}",
                request.RuntimeId, request.Generation, request.AssetKey, workerNodeId);
            if (sensor is { Success: true })
                await RemoveEndpointSensorAsync(workerNodeId, request, CancellationToken.None);
            return TeamLabNodeAssetCreateResult.Failed(exception.Message);
        }
    }

    public Task<TeamLabNodeResult> DestroyAssetAsync(
        Guid workerNodeId,
        TeamLabAssetKind kind,
        string resourceId,
        CancellationToken cancellationToken) => DispatchAsync(
            workerNodeId,
            NodeDispatchCategory.Cleanup,
            operationToken => DestroyAssetCoreAsync(workerNodeId, kind, resourceId, null, operationToken),
            cancellationToken);

    public Task<TeamLabNodeResult> PauseAssetAsync(
        Guid workerNodeId,
        TeamLabAssetKind kind,
        string resourceId,
        int generation,
        CancellationToken cancellationToken) =>
        ChangeAssetLifecycleAsync(workerNodeId, kind, resourceId, generation, pause: true, cancellationToken);

    public Task<TeamLabNodeResult> ResumeAssetAsync(
        Guid workerNodeId,
        TeamLabAssetKind kind,
        string resourceId,
        int generation,
        CancellationToken cancellationToken) =>
        ChangeAssetLifecycleAsync(workerNodeId, kind, resourceId, generation, pause: false, cancellationToken);

    private Task<TeamLabNodeResult> ChangeAssetLifecycleAsync(
        Guid workerNodeId,
        TeamLabAssetKind kind,
        string resourceId,
        int generation,
        bool pause,
        CancellationToken cancellationToken) =>
        DispatchAsync(
            workerNodeId,
            NodeDispatchCategory.Cleanup,
            async operationToken =>
            {
                try
                {
                    var request = new TeamLabAssetLifecycleRequest(
                        kind == TeamLabAssetKind.Docker ? "docker" : "vm",
                        resourceId,
                        generation,
                        _config.DryRun);
                    var response = pause
                        ? await agent.PauseTeamLabAssetAsync(workerNodeId, request, operationToken)
                        : await agent.ResumeTeamLabAssetAsync(workerNodeId, request, operationToken);
                    return RequireMutation(response,
                        pause ? "TeamLab asset pause failed." : "TeamLab asset resume failed.");
                }
                catch (Exception exception) when (exception is AgentClientException or HttpRequestException or TaskCanceledException)
                {
                    return TeamLabNodeResult.Failed(exception.Message);
                }
            },
            cancellationToken);

    public Task<TeamLabScenarioArtifactCommitResult> CommitScenarioArtifactAsync(
        Guid workerNodeId,
        TeamLabScenarioArtifactCommitRequest request,
        CancellationToken cancellationToken) => DispatchAsync(
            workerNodeId,
            NodeDispatchCategory.VmImageTransfer,
            operationToken => CommitScenarioArtifactCoreAsync(workerNodeId, request, operationToken),
            cancellationToken);

    private async Task<TeamLabScenarioArtifactCommitResult> CommitScenarioArtifactCoreAsync(
        Guid workerNodeId,
        TeamLabScenarioArtifactCommitRequest request,
        CancellationToken cancellationToken)
    {
        var response = await agent.CommitVmScenarioAsync(
            workerNodeId,
            new AgentCommitVmScenarioRequest(
                request.OperationId,
                request.VmName,
                request.OsType,
                request.BuildIdentity,
                new AgentVmImageRegistryTarget(
                    request.RegistryAddress,
                    request.RegistryRepository,
                    request.RegistryTag)),
            cancellationToken);
        return new TeamLabScenarioArtifactCommitResult(
            response.Success,
            response.ArtifactDigest,
            response.ArtifactSize,
            response.EvidenceDigest,
            response.RegistryAddress,
            response.Repository,
            response.Tag,
            response.ErrorCode,
            response.ErrorDetail);
    }

    private async Task<TeamLabNodeResult> DestroyAssetCoreAsync(
        Guid workerNodeId,
        TeamLabAssetKind kind,
        string resourceId,
        int? generation,
        CancellationToken cancellationToken)
    {
        try
        {
            if (kind == TeamLabAssetKind.Docker)
                await agent.DestroyContainerAsync(workerNodeId, resourceId, generation, cancellationToken);
            else
                await agent.DestroyVmAsync(workerNodeId, resourceId, generation, null, cancellationToken);
            return TeamLabNodeResult.Ok("Asset destroyed.");
        }
        catch (Exception exception) when (exception is AgentClientException or HttpRequestException or TaskCanceledException)
        {
            return TeamLabNodeResult.Failed(exception.Message);
        }
    }

    public Task<TeamLabNodeResult> CleanupShardAsync(
        Guid workerNodeId,
        TeamLabNodeCleanupRequest request,
        CancellationToken cancellationToken) => DispatchAsync(
            workerNodeId,
            NodeDispatchCategory.Cleanup,
            operationToken => CleanupShardCoreAsync(workerNodeId, request, operationToken),
            cancellationToken);

    private async Task<TeamLabNodeResult> CleanupShardCoreAsync(
        Guid workerNodeId,
        TeamLabNodeCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var containerId in request.ContainerIds.Distinct(StringComparer.Ordinal))
        {
            var result = await DestroyAssetCoreAsync(
                workerNodeId, TeamLabAssetKind.Docker, containerId, request.Generation, cancellationToken);
            if (!result.Success) errors.Add(result.Message);
        }
        foreach (var vmName in request.VmNames.Distinct(StringComparer.Ordinal))
        {
            var result = await DestroyAssetCoreAsync(
                workerNodeId, TeamLabAssetKind.Vm, vmName, request.Generation, cancellationToken);
            if (!result.Success) errors.Add(result.Message);
        }
        var cleanup = await agent.CleanupTeamLabAsync(workerNodeId,
            new GZCTF.Services.Fleet.TeamLabCleanupRequest(
                request.RuntimeId,
                request.Generation,
                request.RouterNamespace,
                request.ResourceNames.Distinct(StringComparer.Ordinal).ToArray(),
                request.SensorAssetKeys.Distinct(StringComparer.Ordinal).ToArray(),
                request.FabricRemoteCidrs.Distinct(StringComparer.Ordinal).ToArray(),
                _config.DryRun),
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
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.Probe,
            operationToken => agent.ProbeTeamLabAsync(workerNodeId,
                new GZCTF.Services.Fleet.TeamLabProbeRequest(
                    request.RuntimeId,
                    request.RouterNamespace,
                    request.TargetIp,
                    request.Kind?.ToString().ToLowerInvariant(),
                    request.Port,
                    _config.DryRun),
                operationToken), cancellationToken);
        return RequireMutation(response, $"Probe to {request.TargetIp} failed.");
    }

    public async Task<TeamLabNodeResult> ConfigureAccessAsync(
        Guid workerNodeId,
        TeamLabNodeAccessApplyRequest request,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.TeamLabNetwork,
            operationToken => agent.ConfigureTeamLabWireGuardAsync(workerNodeId,
                new TeamLabWireGuardRequest(
                    request.RuntimeId,
                    request.Generation,
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
                operationToken), cancellationToken);
        return RequireMutation(response, "Failed to configure TeamLab WireGuard access.");
    }

    public async Task<TeamLabNodeResult> RemoveAccessAsync(
        Guid workerNodeId,
        TeamLabNodeAccessRemoveRequest request,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.Cleanup,
            operationToken => agent.CleanupTeamLabWireGuardAsync(workerNodeId,
                new TeamLabWireGuardCleanupRequest(
                    request.RuntimeId,
                    request.Generation,
                    request.RouterNamespace,
                    request.InterfaceName,
                    _config.DryRun),
                operationToken), cancellationToken);
        return RequireMutation(response, "Failed to remove TeamLab WireGuard access.");
    }

    public async Task<TeamLabNodeObservationResult> ReadObservationsAsync(
        Guid workerNodeId,
        int runtimeId,
        int generation,
        long afterSequence,
        Guid? observationPointId,
        int limit,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.Probe,
            operationToken => agent.ReadTeamLabObservationsAsync(workerNodeId,
                new TeamLabObservationBatchRequest(
                    runtimeId, generation, afterSequence, observationPointId, limit), operationToken),
            cancellationToken);
        if (response is not { Success: true })
            return new TeamLabNodeObservationResult(
                false,
                response?.Message ?? "Traffic observation read failed.",
                afterSequence,
                0,
                [],
                TeamLabNodeObservationHealth.Unavailable);
        return new TeamLabNodeObservationResult(
            true,
            response.Message,
            response.NextSequence,
            response.DroppedCount,
            response.Records.Select(item => new TeamLabNodeObservationRecord(
                item.Sequence,
                item.ObservationPointId,
                item.AssetKey,
                item.CapturedAt,
                item.SourceIp,
                item.SourcePort,
                item.DestinationIp,
                item.DestinationPort,
                item.Protocol,
                item.TcpFlags,
                item.PacketLength,
                item.PacketFingerprint,
                item.FlowFingerprint,
                item.EvidenceKind.ToString(),
                item.ProcessIdentityHash,
                item.Direction,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.Packets,
                item.Bytes)).ToArray(),
            new TeamLabNodeObservationHealth(
                response.Health.Running,
                response.Health.RegisteredPointCount,
                response.Health.ActiveInterfaceCount,
                response.Health.ActiveFlowCount,
                response.Health.DroppedCount,
                response.Health.ParserFailureCount,
                response.Health.SensorRejectedCount,
                response.Health.SpoolBytes,
                response.Health.LastSensorErrorCode,
                response.Health.LastError));
    }

    public async Task<TeamLabNodeCaptureResult> StartCaptureAsync(
        Guid workerNodeId,
        TeamLabNodeCaptureStartRequest request,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.Control,
            operationToken => agent.StartTeamLabCaptureAsync(workerNodeId,
                new TeamLabCaptureStartRequest(
                    request.RuntimeId,
                    request.Generation,
                    request.CaptureId,
                    request.SegmentId,
                    request.ObservationPointId,
                    request.InterfaceToken,
                    request.MaxSeconds,
                    request.MaxBytes,
                    _config.DryRun), operationToken), cancellationToken);
        return ToCaptureResult(response, "Failed to start traffic capture.");
    }

    public async Task<TeamLabNodeCaptureResult> StopCaptureAsync(
        Guid workerNodeId,
        int runtimeId,
        int generation,
        Guid captureId,
        Guid segmentId,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.Control,
            operationToken => agent.StopTeamLabCaptureAsync(workerNodeId,
                new TeamLabCaptureStopRequest(runtimeId, generation, captureId, segmentId, _config.DryRun),
                operationToken), cancellationToken);
        return ToCaptureResult(response, "Failed to stop traffic capture.");
    }

    public async Task<TeamLabNodeCaptureResult> GetCaptureStatusAsync(
        Guid workerNodeId,
        int runtimeId,
        int generation,
        Guid captureId,
        Guid segmentId,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.Probe,
            operationToken => agent.GetTeamLabCaptureStatusAsync(workerNodeId,
                new TeamLabCaptureStatusRequest(runtimeId, generation, captureId, segmentId, _config.DryRun),
                operationToken), cancellationToken);
        return ToCaptureResult(response, "Failed to read traffic capture status.");
    }

    public async Task<TeamLabNodeCaptureResult> UploadCaptureAsync(
        Guid workerNodeId,
        TeamLabNodeCaptureUploadRequest request,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.Control,
            operationToken => agent.UploadTeamLabCaptureAsync(workerNodeId,
                new TeamLabCaptureUploadRequest(
                    request.RuntimeId,
                    request.Generation,
                    request.CaptureId,
                    request.SegmentId,
                    request.UploadPath,
                    request.UploadToken,
                    request.MaxBytes,
                    _config.DryRun), operationToken), cancellationToken);
        return ToCaptureResult(response, "Failed to upload traffic capture.");
    }

    public async Task<TeamLabNodeCaptureResult> DeleteCaptureAsync(
        Guid workerNodeId,
        int runtimeId,
        int generation,
        Guid captureId,
        Guid segmentId,
        CancellationToken cancellationToken)
    {
        var response = await DispatchAsync(workerNodeId, NodeDispatchCategory.Cleanup,
            operationToken => agent.DeleteTeamLabCaptureAsync(workerNodeId,
                new TeamLabCaptureDeleteRequest(
                    runtimeId, generation, captureId, segmentId, _config.DryRun), operationToken),
            cancellationToken);
        return ToCaptureResult(response, "Failed to delete traffic capture segment.");
    }

    private async Task<TeamLabNodeAssetCreateResult> CreateContainerAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        ImageTemplate template,
        string? sensorEndpoint,
        CancellationToken cancellationToken)
    {
        if (template.ImageType != ImageType.Docker)
            return TeamLabNodeAssetCreateResult.Failed($"Image template {template.Id} is not a Docker template.");
        var image = DockerImageReference.ResolvePullTarget(template.Name, template.RegistryUrl).FullImage;
        image = await dockerRegistry.ResolveImageReferenceAsync(image, cancellationToken);
        if (!request.ImageReady)
            await imageDistribution.EnsureDockerImageOnNodeAsync(image, workerNodeId, cancellationToken);
        var environment = request.Environment.Concat(request.Secrets)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var config = new ContainerConfig
        {
            RuntimeId = request.RuntimeId,
            Generation = request.Generation,
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
            StartCommand = request.StartCommand,
            EnableNetworkAdmin = request.RoutingEnabled,
            EnableIpForwarding = request.RoutingEnabled,
            PreferredNodeId = workerNodeId,
            DnsServers = request.Interfaces.SelectMany(item => item.DnsServers).Distinct(StringComparer.Ordinal).ToList(),
            EnvironmentVariables = environment
        };
        if (sensorEndpoint?.StartsWith("unix://", StringComparison.OrdinalIgnoreCase) == true)
        {
            config.BindMounts.Add(new ContainerBindMount
            {
                Source = sensorEndpoint[7..],
                Destination = "/run/gzctf/sensor.sock",
                ReadOnly = false
            });
            config.EnvironmentVariables["GZCTF_SENSOR_RUNTIME_PUBLIC_ID"] = request.RuntimePublicId.ToString("D");
            config.EnvironmentVariables["GZCTF_SENSOR_GENERATION"] = request.Generation.ToString();
            config.EnvironmentVariables["GZCTF_SENSOR_ASSET_KEY"] = request.AssetKey;
            config.EnvironmentVariables["GZCTF_SENSOR_CHANNEL"] = "unix:///run/gzctf/sensor.sock";
            config.EnvironmentVariables["GZCTF_SENSOR_HMAC"] = request.Secrets["GZCTF_SENSOR_HMAC"];
        }
        var container = await agent.CreateContainerOrThrowAsync(workerNodeId, config, cancellationToken);
        var interfaces = request.Interfaces
            .Select((iface, index) => new
            {
                Interface = iface,
                GuestName = TeamLabResourceNameFactory.WorkloadGuestInterface(index)
            })
            .ToArray();
        foreach (var item in interfaces)
        {
            var iface = item.Interface;
            var attach = await agent.AttachTeamLabContainerAsync(workerNodeId,
                new TeamLabContainerAttachRequest(
                    request.RuntimeId,
                    container.ContainerId,
                    iface.BridgeName,
                    TeamLabResourceNameFactory.WorkloadHostInterface(request.RuntimeId, request.AssetKey, iface.Key),
                    item.GuestName,
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
                await agent.DestroyContainerAsync(
                    workerNodeId, container.ContainerId, request.Generation, cancellationToken);
                return TeamLabNodeAssetCreateResult.Failed(attachResult.Message);
            }
        }
        if (request.OperationId is not { } operationId || operationId == Guid.Empty)
        {
            await agent.DestroyContainerAsync(
                workerNodeId, container.ContainerId, request.Generation, cancellationToken);
            return TeamLabNodeAssetCreateResult.Failed("The TeamLab container operation identity is missing.");
        }

        var routes = interfaces
            .SelectMany(item => item.Interface.Routes.Select(route => new TeamLabContainerRouteExpectation(
                route,
                Gateway(item.Interface.IpAddress, item.Interface.PrefixLength),
                item.GuestName)))
            .DistinctBy(route => (route.TargetCidr, route.GatewayIp, route.InterfaceName))
            .ToArray();
        var dnsProbes = interfaces
            .SelectMany(item => item.Interface.DnsServers.Select(server => new TeamLabContainerDnsProbeExpectation(
                server,
                Hostname(request.AssetKey),
                item.Interface.IpAddress)))
            .DistinctBy(probe => (probe.Server, probe.QueryName, probe.ExpectedAddress))
            .ToArray();
        var finalized = await agent.FinalizeTeamLabContainerNetworkAsync(
            workerNodeId,
            new TeamLabContainerNetworkFinalizeRequest(
                operationId,
                request.RuntimeId,
                request.Generation,
                container.ContainerId,
                container.ContainerName,
                interfaces.Select(item => new TeamLabContainerInterfaceExpectation(
                    item.GuestName,
                    $"{item.Interface.IpAddress}/{item.Interface.PrefixLength}",
                    item.Interface.MacAddress)).ToArray(),
                routes,
                request.Interfaces.SelectMany(iface => iface.DnsServers)
                    .Distinct(StringComparer.Ordinal).ToArray(),
                dnsProbes,
                RequireNoDefaultRoute: true,
                _config.DryRun),
            cancellationToken);
        if (finalized is not { Success: true } || finalized.DryRun)
        {
            await agent.DestroyContainerAsync(
                workerNodeId, container.ContainerId, request.Generation, cancellationToken);
            return TeamLabNodeAssetCreateResult.Failed(
                finalized?.Message ?? "The TeamLab container network finalizer returned no result.");
        }
        var sensor = await StartEndpointSensorAsync(
            workerNodeId,
            request,
            container.ContainerId,
            TeamLabEndpointSensorChannelMode.Docker,
            null,
            cancellationToken);
        if (!sensor.Success && request.EndpointObservation == TeamLabEndpointObservationMode.Required)
        {
            await agent.DestroyContainerAsync(
                workerNodeId, container.ContainerId, request.Generation, cancellationToken);
            return TeamLabNodeAssetCreateResult.Failed(sensor.Message);
        }
        return TeamLabNodeAssetCreateResult.Created(container.ContainerId);
    }

    private async Task<TeamLabNodeAssetCreateResult> CreateVmAsync(
        AppDbContext db,
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        ImageTemplate template,
        CancellationToken cancellationToken)
    {
        if (template.ImageType == ImageType.Docker)
            return TeamLabNodeAssetCreateResult.Failed($"Image template {template.Id} is not a VM template.");
        var requiresGuestControl = request.Bootstrap is not null ||
                                   request.EndpointObservation != TeamLabEndpointObservationMode.Disabled;
        if (requiresGuestControl && (template.VmRuntimeMode == VmRuntimeMode.Opaque ||
            template.VmArtifactStatus != VmArtifactStatus.Ready ||
            template.VmRuntimeMode == VmRuntimeMode.Managed &&
            template.PreparedArtifact is not { Status: VmPreparedArtifactStatus.Ready } ||
            !template.CapabilityCertifications.Any(certification =>
                BootstrapProfileCompatibilityService.IsCurrentManagedCertification(certification, template))))
            return TeamLabNodeAssetCreateResult.Failed(
                $"Image template {template.Name} ({template.Id}) cannot provide the requested managed guest capabilities.");
        if (!request.ImageReady)
        {
            var imageReady = await imageDistribution.EnsureVmTemplateOnNodeAsync(
                template.Id, workerNodeId, cancellationToken);
            if (!imageReady.Success) return TeamLabNodeAssetCreateResult.Failed(imageReady.Message);
        }
        var vmName = TeamLabResourceNameFactory.LinuxName($"tl{request.RuntimeId}-{request.AssetKey}");
        var interfaces = BuildVmInterfaces(request, template);
        var bootstrap = await ResolveBootstrapAsync(db, workerNodeId, request, cancellationToken);
        if (request.OperationId is not { } operationId || operationId == Guid.Empty)
            return TeamLabNodeAssetCreateResult.Failed("The TeamLab VM operation identity is missing.");
        var nativeId = StableDomainId(vmName, request.Generation);
        var identity = new GuestAssetIdentity(
            operationId,
            request.RuntimeId,
            request.Generation,
            request.AssetKey,
            vmName,
            nativeId,
            1);
        AgentVmManagementInterfaceConfig? management = null;
        AgentVmGuestSupervisorConfig? supervisor = null;
        if (requiresGuestControl)
        {
            var endpoint = await agent.GetGuestManagementEndpointAsync(workerNodeId, cancellationToken);
            if (!endpoint.Healthy)
                return TeamLabNodeAssetCreateResult.Failed("The Worker guest-management endpoint is not healthy.");
            var artifactDigest = template.VmRuntimeMode == VmRuntimeMode.Scenario
                ? template.ImageHash!
                : template.PreparedArtifact!.ArtifactDigest;
            var (intent, protectedSecrets) = BuildGuestIntent(
                request,
                identity,
                template.OSType,
                artifactDigest,
                bootstrap,
                new Uri($"https://{endpoint.HostAddress}:{endpoint.ListenPort}/api/guest/v1/artifacts"));
            var enrollment = await agent.PrepareGuestControlAsync(
                workerNodeId,
                new GuestControlPrepareRequest(
                    identity,
                    intent,
                    DateTimeOffset.UtcNow.AddMinutes(15),
                    protectedSecrets),
                cancellationToken);
            management = new AgentVmManagementInterfaceConfig
            {
                Identity = identity,
                BridgeName = enrollment.ManagementLease.BridgeName,
                MacAddress = enrollment.ManagementLease.MacAddress,
                IpAddress = enrollment.ManagementLease.GuestAddress,
                PrefixLength = enrollment.ManagementLease.PrefixLength,
                HostAddress = enrollment.ManagementLease.HostAddress,
                Model = "e1000e"
            };
            supervisor = new AgentVmGuestSupervisorConfig
            {
                Identity = identity,
                EnrollmentToken = enrollment.EnrollmentToken,
                WorkerServerCertificateSha256 = enrollment.WorkerServerCertificateSha256,
                EnrollmentEndpoint = enrollment.EnrollmentEndpoint.ToString(),
                IntentDigest = intent.IntentDigest
            };
        }
        var vmRequest = new AgentCreateVmRequest
        {
            OperationId = operationId,
            RuntimeId = request.RuntimeId,
            Generation = request.Generation,
            TemplateId = template.Id,
            TemplatePath = null,
            ImageEnsured = true,
            VmName = vmName,
            Memory = request.MemoryMiB,
            Cpu = CpuUnitsToVcpu(request.CpuUnits),
            Flag = request.Secrets.GetValueOrDefault("FLAG") ?? request.Secrets.GetValueOrDefault("GZCTF_FLAG"),
            Interfaces = interfaces,
            CloudInit = new AgentVmInitConfig
            {
                Enabled = template.VmRuntimeMode == VmRuntimeMode.Managed,
                OsType = template.OSType,
                Hostname = vmName,
                InstanceId = $"teamlab-{request.RuntimeId}-{request.AssetKey}",
                NetworkMode = template.VmNetworkMode
            },
            GuestControl = new AgentVmGuestControlConfig
            {
                Enabled = requiresGuestControl,
                Required = requiresGuestControl,
                EndpointSensorChannel = false,
                OsType = template.OSType
            },
            ManagementInterface = management,
            GuestSupervisor = supervisor
        };
        var vm = await agent.CreateVmAsync(workerNodeId, vmRequest, cancellationToken);
        if (vm is null || string.IsNullOrWhiteSpace(vm.VmName))
            return TeamLabNodeAssetCreateResult.Failed($"Failed to create VM {request.Name}.");
        return TeamLabNodeAssetCreateResult.Created(vm.VmName, vm.NativeId);
    }

    public async Task<TeamLabNodeResult> WaitForAssetReadyAsync(
        Guid workerNodeId,
        string runtimeResourceId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind != TeamLabAssetKind.Vm)
            return TeamLabNodeResult.Ok("Container network readiness is completed during creation.");
        if (request.OperationId is not { } operationId || operationId == Guid.Empty)
            return TeamLabNodeResult.Failed("The TeamLab VM readiness operation identity is missing.");
        if (!RequiresGuestControl(request))
            return TeamLabNodeResult.Ok("VM domain is running; host-side health probing will determine readiness.");

        var guest = await runtimeSignals.WaitForAsync(
            operationId,
            request.Generation,
            AgentRuntimeSignalStage.NetworkApplied,
            BoundedTimeout(_config.ManagedVmNetworkReadyTimeoutSeconds),
            cancellationToken);
        if (!guest.Ready)
            return TeamLabNodeResult.Failed(
                $"VM {request.Name} guest control did not become ready: " +
                $"{guest.ErrorCode ?? "runtime.guest_ready_signal_missing"}");
        return TeamLabNodeResult.Ok("VM guest management link, enrollment, and network are ready.");
    }

    internal static int CpuUnitsToVcpu(int cpuUnits) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(1, cpuUnits) / 10d));

    private async Task<TeamLabNodeResult> StartEndpointSensorAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        string runtimeResourceId,
        TeamLabEndpointSensorChannelMode mode,
        OSType? osType,
        CancellationToken cancellationToken)
    {
        if (request.EndpointObservation == TeamLabEndpointObservationMode.Disabled)
            return TeamLabNodeResult.Ok("Endpoint observation is disabled.");
        var response = await agent.StartTeamLabEndpointSensorAsync(
            workerNodeId,
            new TeamLabEndpointSensorStartRequest(
                request.RuntimeId,
                request.Generation,
                request.AssetKey,
                runtimeResourceId,
                mode,
                osType),
            cancellationToken);
        if (response is { Success: true })
            return TeamLabNodeResult.Ok(response.Message);
        var message = response?.Message ?? "Endpoint sensor could not be started.";
        if (request.EndpointObservation == TeamLabEndpointObservationMode.Optional)
            logger.LogWarning(
                "可选的 TeamLab endpoint sensor 不可用: runtime={RuntimeId}, generation={Generation}, asset={AssetKey}, node={NodeId}, reason={Reason}",
                request.RuntimeId, request.Generation, request.AssetKey, workerNodeId, message);
        return TeamLabNodeResult.Failed(message);
    }

    private async Task<TeamLabEndpointSensorResponse?> RegisterEndpointSensorAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.EndpointObservation == TeamLabEndpointObservationMode.Disabled) return null;
        if (!request.Secrets.TryGetValue("GZCTF_SENSOR_HMAC", out var credential) ||
            string.IsNullOrWhiteSpace(credential))
            return new TeamLabEndpointSensorResponse(false, "Endpoint sensor credential is unavailable.");
        var resourceId = request.Kind == TeamLabAssetKind.Vm
            ? TeamLabResourceNameFactory.LinuxName($"tl{request.RuntimeId}-{request.AssetKey}")
            : request.AssetKey;
        return await agent.RegisterTeamLabEndpointSensorAsync(
            workerNodeId,
            new TeamLabEndpointSensorRegistrationRequest(
                request.RuntimeId,
                request.RuntimePublicId.ToString("D"),
                request.Generation,
                request.AssetKey,
                resourceId,
                1,
                credential,
                request.Kind == TeamLabAssetKind.Vm
                    ? TeamLabEndpointSensorChannelMode.Vm
                    : TeamLabEndpointSensorChannelMode.Docker),
            cancellationToken);
    }

    private async Task RemoveEndpointSensorAsync(
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken) =>
        await agent.RemoveTeamLabEndpointSensorAsync(
            workerNodeId,
            new TeamLabEndpointSensorRemoveRequest(
                request.RuntimeId, request.Generation, request.AssetKey),
            cancellationToken);

    public async Task<TeamLabNodeBootstrapResult> ApplyBootstrapAsync(
        Guid workerNodeId,
        string runtimeResourceId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind == TeamLabAssetKind.Docker)
            return request.Bootstrap is null
                ? TeamLabNodeBootstrapResult.Completed()
                : TeamLabNodeBootstrapResult.Failed(
                    "Docker bootstrap profiles require the container bootstrap executor delivered with the endpoint observer unit.");
        if (request.Bootstrap is null)
            return TeamLabNodeBootstrapResult.Completed();
        if (request.OperationId is not { } operationId || operationId == Guid.Empty)
            return TeamLabNodeBootstrapResult.Failed("The TeamLab VM bootstrap operation identity is missing.");
        var bootstrapTimeout = await ResolveBootstrapSignalTimeoutAsync(request, cancellationToken);
        var completed = await runtimeSignals.WaitForAsync(
            operationId,
            request.Generation,
            AgentRuntimeSignalStage.BootstrapCompleted,
            bootstrapTimeout,
            cancellationToken);
        if (completed.Ready)
            return TeamLabNodeBootstrapResult.Completed();

        var errorCode = completed.ErrorCode ?? "runtime.bootstrap_signal_missing";
        var detail = completed.Facts is { Count: > 0 }
            ? string.Join(", ", completed.Facts.OrderBy(item => item.Key)
                .Select(item => $"{item.Key}={item.Value}"))
            : null;
        return TeamLabNodeBootstrapResult.Failed(
            detail is null
                ? $"VM bootstrap did not complete: {errorCode}"
                : $"VM bootstrap did not complete: {errorCode} ({detail})");
    }

    public async Task<TeamLabNodeBootstrapResult> ProbeAssetHealthAsync(
        Guid workerNodeId,
        string runtimeResourceId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind == TeamLabAssetKind.Docker)
        {
            var primary = request.Interfaces.First(item => item.Primary);
            TeamLabNodeResult? probe = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                probe = await ProbeAsync(workerNodeId,
                    new TeamLabNodeProbeRequest(
                        request.RuntimeId,
                        request.RouterNamespace,
                        primary.IpAddress,
                        request.Health?.Kind,
                        request.Health?.Port), cancellationToken);
                if (probe.Success)
                    return TeamLabNodeBootstrapResult.Completed(
                        healthChecks: request.Health is null
                            ? []
                            : [$"{request.Health.Kind}:{request.Health.Port}"]);
                if (attempt < 29)
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            return TeamLabNodeBootstrapResult.Failed(
                probe?.Message ?? $"Container {request.Name} did not become reachable.");
        }

        if (!RequiresGuestControl(request))
        {
            var opaquePrimaryIp = request.Interfaces.First(item => item.Primary).IpAddress;
            TeamLabNodeResult? probe = null;
            for (var attempt = 0; attempt < 120; attempt++)
            {
                probe = await ProbeAsync(workerNodeId,
                    new TeamLabNodeProbeRequest(
                        request.RuntimeId,
                        request.RouterNamespace,
                        opaquePrimaryIp,
                        request.Health?.Kind,
                        request.Health?.Port), cancellationToken);
                if (probe.Success)
                    return TeamLabNodeBootstrapResult.Completed(
                        healthChecks: request.Health is null ? [] : [$"{request.Health.Kind}:{request.Health.Port}"]);
                if (attempt < 119)
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            return TeamLabNodeBootstrapResult.Failed(
                probe?.Message ?? $"VM {request.Name} did not become reachable from its router namespace.");
        }
        if (request.OperationId is not { } operationId || operationId == Guid.Empty)
            return TeamLabNodeBootstrapResult.Failed("The TeamLab VM health operation identity is missing.");
        var lifecycle = await runtimeSignals.WaitForAsync(
            operationId,
            request.Generation,
            AgentRuntimeSignalStage.ObservationReady,
            BoundedTimeout(_config.ManagedVmObservationReadyTimeoutSeconds),
            cancellationToken);
        if (!lifecycle.Ready)
            return TeamLabNodeBootstrapResult.Failed(
                $"VM lifecycle health did not become ready: {lifecycle.ErrorCode ?? "runtime.health_signal_missing"}");
        IReadOnlyList<string> passedHealth = [];
        var primaryIp = request.Interfaces.First(item => item.Primary).IpAddress;
        if (request.Health is not null)
        {
            var probe = await ProbeAsync(workerNodeId,
                new TeamLabNodeProbeRequest(
                    request.RuntimeId,
                    request.RouterNamespace,
                    primaryIp,
                    request.Health.Kind,
                    request.Health.Port), cancellationToken);
            if (!probe.Success)
                return TeamLabNodeBootstrapResult.Failed(
                    $"VM {request.Name} health check failed: {probe.Message}");
            passedHealth = [$"{request.Health.Kind}:{request.Health.Port}"];
        }
        return TeamLabNodeBootstrapResult.Completed(healthChecks: passedHealth);
    }

    private static List<AgentVmNetworkInterfaceRequest> BuildVmInterfaces(
        TeamLabNodeAssetCreateRequest request,
        ImageTemplate template) => request.Interfaces.Select((iface, index) => new AgentVmNetworkInterfaceRequest
    {
        BridgeName = iface.BridgeName,
        HostInterfaceName = TeamLabResourceNameFactory.WorkloadHostInterface(
            request.RuntimeId, request.AssetKey, iface.Key),
        MacAddress = iface.MacAddress,
        Model = template.OSType == OSType.Windows ? "e1000e" : "virtio",
        InterfaceName = TeamLabResourceNameFactory.WorkloadGuestInterface(index),
        IpAddress = iface.IpAddress,
        PrefixLength = iface.PrefixLength,
        Gateway = iface.Primary ? Gateway(iface.IpAddress, iface.PrefixLength) : null,
        DnsServers = iface.DnsServers.ToList(),
        Routes = template.VmRuntimeMode == VmRuntimeMode.Managed &&
                 template.VmNetworkMode == VmNetworkMode.Dhcp
            ? iface.Routes.Select(route => $"{route} via {Gateway(iface.IpAddress, iface.PrefixLength)}").ToList()
            : [],
        IsPrimary = iface.Primary
    }).ToList();

    private static bool RequiresGuestControl(TeamLabNodeAssetCreateRequest request) =>
        request.Bootstrap is not null || request.EndpointObservation != TeamLabEndpointObservationMode.Disabled;

    private async Task<TimeSpan> ResolveBootstrapSignalTimeoutAsync(
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Bootstrap is null)
            return BoundedTimeout(_config.ManagedVmBootstrapOverheadSeconds);

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manifestJson = await context.BootstrapProfileVersions.AsNoTracking()
            .Where(item =>
                item.Profile.PublicId == request.Bootstrap.ProfileId &&
                item.Version == request.Bootstrap.Version &&
                item.Status == BootstrapProfileVersionStatus.Ready)
            .Select(item => item.ManifestJson)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Bootstrap profile {request.Bootstrap.ProfileId:D} v{request.Bootstrap.Version} is not ready.");

        var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(manifestJson);
        var declaredSeconds = (long)Math.Max(1, _config.ManagedVmBootstrapOverheadSeconds) +
                              manifest.Steps.Sum(item => (long)item.TimeoutSeconds) +
                              manifest.HealthChecks.Sum(item => (long)item.TimeoutSeconds * item.Attempts) +
                              (long)manifest.MaxReboots * Math.Max(1, _config.ManagedVmRebootAllowanceSeconds);
        var maximumSeconds = Math.Max(1, _config.ManagedVmMaximumBootstrapTimeoutSeconds);
        if (declaredSeconds > maximumSeconds)
            throw new InvalidOperationException(
                $"Bootstrap profile {request.Bootstrap.ProfileId:D} v{request.Bootstrap.Version} declares " +
                $"{declaredSeconds} seconds of runtime work, exceeding the {maximumSeconds}-second online limit. " +
                "Mark the asset BakeAtPublish and publish a scenario artifact instead.");

        return TimeSpan.FromSeconds(declaredSeconds);
    }

    private static TimeSpan BoundedTimeout(int seconds) => TimeSpan.FromSeconds(Math.Max(1, seconds));

    private async Task<ResolvedBootstrap?> ResolveBootstrapAsync(
        AppDbContext context,
        Guid workerNodeId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Bootstrap is null) return null;
        var version = await context.BootstrapProfileVersions
            .Include(item => item.Profile)
            .SingleOrDefaultAsync(item =>
                    item.Profile.PublicId == request.Bootstrap.ProfileId &&
                    item.Version == request.Bootstrap.Version &&
                    item.Status == BootstrapProfileVersionStatus.Ready,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Bootstrap profile {request.Bootstrap.ProfileId:D} v{request.Bootstrap.Version} is not ready.");
        var distributed = await context.BootstrapProfileDistributions.AsNoTracking().AnyAsync(item =>
                item.ProfileVersionId == version.Id &&
                item.WorkerNodeId == workerNodeId &&
                item.Status == BootstrapProfileDistributionStatus.Ready &&
                item.ArtifactDigest == version.ArtifactDigest,
            cancellationToken);
        if (!distributed)
            throw new InvalidOperationException(
                $"Bootstrap profile {request.Bootstrap.ProfileId:D} v{request.Bootstrap.Version} is not ready on the assigned node.");

        var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(version.ManifestJson);
        if (string.IsNullOrWhiteSpace(version.ManifestSignature) ||
            string.IsNullOrWhiteSpace(version.SigningPublicKeyPem))
        {
            var signed = BootstrapProfileOperationHandler.SignManifest(version.ManifestJson);
            version.ManifestSignature = signed.Signature;
            version.SigningPublicKeyPem = signed.PublicKeyPem;
            await context.SaveChangesAsync(cancellationToken);
        }
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in manifest.Parameters)
        {
            var value = definition.Secret
                ? request.Secrets.GetValueOrDefault(definition.Key)
                : request.Bootstrap.Parameters.GetValueOrDefault(definition.Key) ?? definition.DefaultValue;
            if (definition.Required && string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    $"Bootstrap parameter '{definition.Key}' is required for asset {request.AssetKey}.");
            if (value is null) continue;
            (definition.Secret ? secrets : parameters)[definition.Key] = value;
        }
        return new ResolvedBootstrap(
            version.Profile.PublicId,
            version.Version,
            version.ArtifactDigest,
            version.ArtifactSize,
            version.ManifestJson,
            version.ManifestSignature,
            version.SigningPublicKeyPem,
            parameters,
            secrets);
    }

    private static (GuestBootstrapIntent Intent, IReadOnlyDictionary<string, string> Secrets) BuildGuestIntent(
        TeamLabNodeAssetCreateRequest request,
        GuestAssetIdentity identity,
        OSType osType,
        string preparedArtifactDigest,
        ResolvedBootstrap? bootstrap,
        Uri artifactEndpoint)
    {
        var runtimeEnvironment = request.Environment
            .Append(new KeyValuePair<string, string>("GZCTF_RUNTIME_ID", request.RuntimeId.ToString()))
            .Append(new KeyValuePair<string, string>("GZCTF_TOPOLOGY_KEY", request.AssetKey))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var parameters = (bootstrap?.Parameters ?? new Dictionary<string, string>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Value, StringComparer.Ordinal);
        // A service package accepts only its manifest-declared secret parameters.
        // Runtime-owned secrets, such as the endpoint sensor credential, remain on
        // their dedicated Agent path and must not become package template values.
        var suppliedSecrets = (bootstrap?.Secrets ?? request.Secrets)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var references = new List<GuestSecretReference>();
        var protectedSecrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in suppliedSecrets)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
                throw new InvalidOperationException("guest_secret_name_invalid");
            var reference = "secret:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{identity.OperationId:D}:{identity.Generation}:{identity.AssetKey}:{name}")));
            var target = osType == OSType.Windows
                ? $@"C:\ProgramData\GZCTF\Runtime\secrets\{name}"
                : $"/opt/gzctf/runtime/secrets/{name}";
            references.Add(new GuestSecretReference(name, reference, target));
            protectedSecrets[reference] = value;
        }
        var servicePackage = bootstrap is null
            ? null
            : new GuestServicePackageDescriptor(
                bootstrap.ProfileId,
                bootstrap.Version,
                bootstrap.ArtifactDigest,
                bootstrap.ArtifactSize,
                artifactEndpoint,
                bootstrap.ManifestJson,
                bootstrap.ManifestSignature,
                bootstrap.SigningPublicKeyPem);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var draft = new GuestBootstrapIntent(
            GuestControlProtocol.SchemaVersion,
            GuestControlProtocol.SchemaVersion,
            identity,
            string.Empty,
            preparedArtifactDigest,
            bootstrap?.ArtifactDigest,
            expiresAt,
            servicePackage,
            references,
            parameters,
            runtimeEnvironment);
        var digest = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(draft)));
        return (draft with { IntentDigest = digest }, protectedSecrets);
    }

    private static Guid StableDomainId(string vmName, int generation)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"gzctf-vm:{vmName}:{Math.Max(1, generation)}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static TeamLabStaticRouteRequest ToAgentRoute(TeamLabNodeRouteIntent route) =>
        new(route.TargetCidr, route.GatewayIp, route.SourceIp);

    private TeamLabNodeResult RequireMutation(TeamLabDryRunResponse? response, string fallback) =>
        response is not { Success: true }
            ? TeamLabNodeResult.Failed(response?.Message ?? fallback)
            : response.DryRun
                ? TeamLabNodeResult.Failed(response.Message)
                : TeamLabNodeResult.Ok(response.Message);

    private TeamLabNodeResult RequireMutation(TeamLabAssetLifecycleResponse? response, string fallback) =>
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
                response?.SegmentId ?? Guid.Empty,
                response?.CapturedBytes ?? 0,
                false,
                response?.Sha256,
                response?.Uploaded ?? false)
            : new TeamLabNodeCaptureResult(
                true,
                response.Message,
                response.SegmentId,
                response.CapturedBytes,
                response.Running,
                response.Sha256,
                response.Uploaded);

    private async Task<T> DispatchAsync<T>(
        Guid workerNodeId,
        NodeDispatchCategory category,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var limits = await _dispatchLimits
            .GetOrAdd(workerNodeId, LoadDispatchLimitsAsync)
            .WaitAsync(cancellationToken);
        return await dispatchLimiter.RunAsync(
            workerNodeId,
            category,
            NodeDispatchLimitPolicy.Resolve(limits, category),
            operation,
            cancellationToken);
    }

    private async Task<AgentExecutionLimits?> LoadDispatchLimitsAsync(Guid workerNodeId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manifestJson = await db.WorkerNodes.AsNoTracking()
            .Where(item => item.Id == workerNodeId)
            .Select(item => item.CapabilityManifestJson)
            .SingleOrDefaultAsync(CancellationToken.None);
        return AgentCapabilityEvaluator.Parse(manifestJson)?.ExecutionLimits;
    }

    private sealed record ResolvedBootstrap(
        Guid ProfileId,
        int Version,
        string ArtifactDigest,
        long ArtifactSize,
        string ManifestJson,
        string ManifestSignature,
        string SigningPublicKeyPem,
        IReadOnlyDictionary<string, string> Parameters,
        IReadOnlyDictionary<string, string> Secrets);

    private static string Hostname(string value) => new(value.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
    private static int StableId(string value) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0) & int.MaxValue;
    private static string Gateway(string ipAddress, int prefix)
    {
        var bytes = System.Net.IPAddress.Parse(ipAddress).GetAddressBytes();
        var raw = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var mask = uint.MaxValue << (32 - prefix);
        var gateway = (raw & mask) + 1;
        return new System.Net.IPAddress(new[] { (byte)(gateway >> 24), (byte)(gateway >> 16), (byte)(gateway >> 8), (byte)gateway }).ToString();
    }
}
