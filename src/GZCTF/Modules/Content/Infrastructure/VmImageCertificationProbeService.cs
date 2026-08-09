using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.GuestControl.Contracts;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed record VmImageCertificationProbeResult(
    bool Success,
    Guid WorkerNodeId,
    string EvidenceDigest,
    IReadOnlyList<string> VerifiedCapabilities,
    string? ErrorCode,
    string? ErrorDetail,
    long DomainCreateDurationMs,
    long GuestReadyDurationMs,
    long FullProbeDurationMs);

public sealed class VmImageCertificationProbeService(
    AppDbContext context,
    ImageDistributionService imageDistribution,
    AgentClient agent,
    PreparedImageConformancePackageFactory packages,
    ILogger<VmImageCertificationProbeService> logger)
{
    public async Task<VmImageCertificationProbeResult> ProbeAsync(
        ImageTemplate template,
        IReadOnlyList<string> requestedCapabilities,
        CancellationToken cancellationToken)
    {
        if (template.ImageType == ImageType.Docker)
            throw new InvalidOperationException("Controlled guest certification requires a VM image template.");
        if (template.VmRuntimeMode == VmRuntimeMode.Scenario ||
            template.VmArtifactStatus != VmArtifactStatus.Ready ||
            template.PreparedArtifact is not { Status: VmPreparedArtifactStatus.Ready } prepared ||
            !string.Equals(template.ImageHash, prepared.ArtifactDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Controlled certification accepts only immutable qcow2 imports with verified Registry provenance.");
        var requiredCapabilities = requestedCapabilities.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (!HasManagedBaseline(template.OSType, requiredCapabilities))
            throw new InvalidOperationException(
                "Controlled certification request does not include the managed-image baseline capabilities.");

        var now = DateTimeOffset.UtcNow;
        var requiredScratchBytes = checked(template.FileSize + 2L * 1024 * 1024 * 1024);
        var nodes = (await context.WorkerNodes.AsNoTracking()
                .Where(item => item.Status == NodeStatus.Online && item.IsSchedulable &&
                               (item.Capabilities & NodeCapability.Kvm) != 0 &&
                               item.CurrentVms < item.MaxVms)
                .ToArrayAsync(cancellationToken))
            .Select(item => (Node: item, Manifest: AgentCapabilityEvaluator.Parse(item.CapabilityManifestJson)))
            .Where(item => item.Node.GetEffectiveStatus(now) == NodeStatus.Online &&
                           item.Manifest is not null &&
                           item.Manifest.Host.AvailableVmImageStorageBytes >= requiredScratchBytes &&
                           AgentCapabilityEvaluator.Supports(item.Node, AgentFeatureIds.VmDownload) &&
                           AgentCapabilityEvaluator.Supports(item.Node, AgentFeatureIds.VmGuestManagement) &&
                           AgentCapabilityEvaluator.Supports(item.Node, AgentFeatureIds.VmConfigDriveV2) &&
                           AgentCapabilityEvaluator.Supports(item.Node, AgentFeatureIds.VmPreparedImage) &&
                           AgentCapabilityEvaluator.Supports(item.Node, AgentFeatureIds.RuntimeSignals))
            .OrderBy(item => item.Node.MaxVms <= 0 ? 1d : (double)item.Node.CurrentVms / item.Node.MaxVms)
            .ThenBy(item => item.Node.CpuLoad)
            .ThenBy(item => item.Node.MemoryLoad)
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Node.Id)
            .Select(item => item.Node)
            .ToArray();
        var node = nodes.FirstOrDefault()
                   ?? throw new InvalidOperationException(
                       $"No online KVM Worker supports prepared-image Guest Supervisor conformance with {requiredScratchBytes} bytes of local image capacity.");
        var image = await imageDistribution.EnsureVmTemplateForCertificationOnNodeAsync(
            template.Id, node.Id, cancellationToken);
        if (!image.Success)
            throw new InvalidOperationException(
                $"Template {template.Name} could not be prepared on node {node.Name}: {image.Message}");

        var package = packages.Create(template.OSType);
        await agent.StageGuestConformancePackageAsync(
            node.Id,
            new AgentGuestConformancePackageRequest(
                package.ProfileId,
                package.Version,
                package.ArtifactDigest,
                Convert.ToBase64String(package.Artifact)),
            cancellationToken);
        var operationId = Guid.CreateVersion7();
        var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var vmName = $"cert-{template.Id}-{suffix}";
        var nativeId = StableDomainId(vmName, 1);
        var identity = new GuestAssetIdentity(
            operationId,
            Math.Max(1, template.Id),
            1,
            $"conformance-{template.Id}",
            vmName,
            nativeId,
            1);
        AgentCreateVmResponse? created = null;
        var fullTimer = Stopwatch.StartNew();
        var domainDuration = 0L;
        var guestDuration = 0L;
        try
        {
            var endpoint = await agent.GetGuestManagementEndpointAsync(node.Id, cancellationToken);
            if (!endpoint.Healthy)
                return Failed(node.Id, template, "guest_management_network_not_ready",
                    "The Worker guest-management endpoint is unhealthy.");
            var artifactEndpoint = new Uri(
                $"https://{endpoint.HostAddress}:{endpoint.ListenPort}/api/guest/v1/artifacts");
            var descriptor = new GuestServicePackageDescriptor(
                package.ProfileId,
                package.Version,
                package.ArtifactDigest,
                package.Artifact.LongLength,
                artifactEndpoint,
                package.ManifestJson,
                package.ManifestSignature,
                package.SigningPublicKeyPem);
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
            var draft = new GuestBootstrapIntent(
                GuestControlProtocol.SchemaVersion,
                GuestControlProtocol.SchemaVersion,
                identity,
                string.Empty,
                prepared.ArtifactDigest,
                package.ArtifactDigest,
                expiresAt,
                descriptor,
                [],
                new Dictionary<string, string>());
            var intent = draft with
            {
                IntentDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(draft)))
            };
            var enrollment = await agent.PrepareGuestControlAsync(
                node.Id,
                new GuestControlPrepareRequest(identity, intent, expiresAt, null, ProjectRuntimeSignals: false),
                cancellationToken);
            var domainTimer = Stopwatch.StartNew();
            created = await agent.CreateVmAsync(node.Id, new AgentCreateVmRequest
            {
                OperationId = operationId,
                RuntimeId = identity.RuntimeId,
                Generation = identity.Generation,
                TemplateId = template.Id,
                ImageEnsured = true,
                VmName = vmName,
                Memory = template.OSType == OSType.Windows ? 4096 : 2048,
                Cpu = 2,
                DefaultNetworkModel = template.OSType == OSType.Windows ? "e1000e" : "virtio",
                CloudInit = new AgentVmInitConfig
                {
                    Enabled = true,
                    OsType = template.OSType,
                    Hostname = vmName,
                    InstanceId = $"conformance-{operationId:N}"
                },
                GuestControl = new AgentVmGuestControlConfig
                {
                    Enabled = true,
                    Required = true,
                    EndpointSensorChannel = false,
                    OsType = template.OSType
                },
                ManagementInterface = new AgentVmManagementInterfaceConfig
                {
                    Identity = identity,
                    BridgeName = enrollment.ManagementLease.BridgeName,
                    HostAddress = enrollment.ManagementLease.HostAddress,
                    PrefixLength = enrollment.ManagementLease.PrefixLength,
                    IpAddress = enrollment.ManagementLease.GuestAddress,
                    MacAddress = enrollment.ManagementLease.MacAddress,
                    Model = "e1000e"
                },
                GuestSupervisor = new AgentVmGuestSupervisorConfig
                {
                    Identity = identity,
                    EnrollmentToken = enrollment.EnrollmentToken,
                    WorkerServerCertificateSha256 = enrollment.WorkerServerCertificateSha256,
                    EnrollmentEndpoint = enrollment.EnrollmentEndpoint.ToString(),
                    IntentDigest = intent.IntentDigest
                }
            }, cancellationToken);
            domainTimer.Stop();
            domainDuration = domainTimer.ElapsedMilliseconds;
            if (created is null)
                return Failed(node.Id, template, "probe_domain_create_failed",
                    "Agent did not return a temporary conformance VM.");
            var guestTimer = Stopwatch.StartNew();
            var status = await WaitForObservationAsync(node.Id, identity, cancellationToken);
            guestTimer.Stop();
            guestDuration = guestTimer.ElapsedMilliseconds;
            if (status.LastStage == GuestLifecycleStage.Failed)
                return Failed(node.Id, template, "guest_supervisor_conformance_failed",
                    "Guest Supervisor reported a terminal conformance failure.", domainDuration, guestDuration,
                    fullTimer.ElapsedMilliseconds);
            if (status.LastStage != GuestLifecycleStage.ObservationReady)
                return Failed(node.Id, template, "guest_supervisor_observation_missing",
                    "Guest Supervisor did not complete the conformance lifecycle.", domainDuration, guestDuration,
                    fullTimer.ElapsedMilliseconds);
            if (!await agent.WaitVmCleanShutdownAsync(node.Id, vmName, 120, cancellationToken))
                return Failed(node.Id, template, "guest_conformance_clean_shutdown_missing",
                    "The conformance package did not produce a clean domain shutdown.", domainDuration, guestDuration,
                    fullTimer.ElapsedMilliseconds);
            fullTimer.Stop();
            return new VmImageCertificationProbeResult(
                true,
                node.Id,
                EvidenceDigest(template, node.Id, requiredCapabilities, status, null, null),
                requiredCapabilities,
                null,
                null,
                domainDuration,
                guestDuration,
                fullTimer.ElapsedMilliseconds);
        }
        catch (Exception exception) when (
            exception is AgentClientException or HttpRequestException or TaskCanceledException or InvalidOperationException &&
            !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception,
                "Prepared VM conformance failed: Template={TemplateId}, Node={NodeId}, VM={VmName}",
                template.Id, node.Id, vmName);
            return Failed(node.Id, template, "probe_transport_failed", exception.Message,
                domainDuration, guestDuration, fullTimer.ElapsedMilliseconds);
        }
        finally
        {
            try
            {
                await agent.DestroyVmAsync(
                    node.Id,
                    vmName,
                    identity.Generation,
                    identity.NativeVmId.ToString("D"),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Temporary conformance VM cleanup failed: Template={TemplateId}, Node={NodeId}, VM={VmName}",
                    template.Id, node.Id, vmName);
            }
        }
    }

    private async Task<GuestControlStatus> WaitForObservationAsync(
        Guid nodeId,
        GuestAssetIdentity identity,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        while (true)
        {
            var status = await agent.GetGuestControlStatusAsync(nodeId, identity, deadline.Token);
            if (status?.LastStage is GuestLifecycleStage.ObservationReady or GuestLifecycleStage.Failed)
                return status;
            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
        }
    }

    private static bool HasManagedBaseline(OSType osType, IReadOnlyCollection<string> capabilities)
    {
        var osInit = osType == OSType.Windows
            ? ImageTemplateCapabilityIds.WindowsCloudbaseInit
            : ImageTemplateCapabilityIds.LinuxCloudInitNoCloud;
        var network = osType == OSType.Windows
            ? ImageTemplateCapabilityIds.NetworkE1000E
            : ImageTemplateCapabilityIds.NetworkVirtio;
        return capabilities.Contains(osInit) && capabilities.Contains(network) &&
               capabilities.Contains(ImageTemplateCapabilityIds.GuestSupervisor) &&
               capabilities.Contains(ImageTemplateCapabilityIds.VmPreparedImage);
    }

    private static VmImageCertificationProbeResult Failed(
        Guid nodeId,
        ImageTemplate template,
        string code,
        string detail,
        long domainCreateDurationMs = 0,
        long guestReadyDurationMs = 0,
        long fullProbeDurationMs = 0) => new(
        false,
        nodeId,
        EvidenceDigest(template, nodeId, [], null, code, detail),
        [],
        code,
        detail,
        domainCreateDurationMs,
        guestReadyDurationMs,
        fullProbeDurationMs);

    private static string EvidenceDigest(
        ImageTemplate template,
        Guid nodeId,
        IReadOnlyList<string> capabilities,
        GuestControlStatus? status,
        string? errorCode,
        string? errorDetail)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            template.Id,
            template.ImageHash,
            template.PreparedArtifactId,
            NodeId = nodeId,
            Capabilities = capabilities.Order(StringComparer.Ordinal).ToArray(),
            status?.LastSequence,
            status?.LastPayloadDigest,
            status?.LastStage,
            errorCode,
            errorDetail
        });
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private static Guid StableDomainId(string vmName, int generation)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"gzctf-vm:{vmName}:{Math.Max(1, generation)}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
