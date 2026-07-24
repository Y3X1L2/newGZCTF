using System.Security.Cryptography;
using System.Text;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabScenarioBakeService(
    AppDbContext context,
    TeamLabRuntimePlanner planner,
    ITeamLabRuntimeQueue queue,
    ITeamLabNodeExecutor executor,
    TeamLabRuntimeCleanupService cleanup,
    IOptions<DockerRegistrySettings> registryOptions,
    ILogger<TeamLabScenarioBakeService> logger)
{
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromSeconds(2);
    private readonly DockerRegistrySettings _registry = registryOptions.Value;

    public async Task EnsureReleaseReadyAsync(
        Guid releaseId,
        Guid actorUserId,
        Guid publishOperationId,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? scenarioOverlays,
        CancellationToken cancellationToken)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releaseId, cancellationToken)
            ?? throw Terminal("release_not_found", "The topology release was not found for scenario baking.");
        var definition = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        var bakeAssets = definition.Assets.Where(item => item.BakeAtPublish)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        if (bakeAssets.Length == 0) return;
        scenarioOverlays = ValidateScenarioOverlays(bakeAssets, scenarioOverlays);

        var sourceTemplateIds = bakeAssets.Select(item => item.ImageTemplateId).Distinct().ToArray();
        var sourceTemplates = await context.ImageTemplates
            .Include(item => item.PreparedArtifact)
            .Include(item => item.CapabilityCertifications)
            .Where(item => sourceTemplateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        ValidateSources(bakeAssets, sourceTemplates);

        var artifacts = await EnsureArtifactRowsAsync(
            release,
            bakeAssets,
            sourceTemplates,
            cancellationToken);
        if (artifacts.All(IsReady))
        {
            await EnsureRuntimeCleanedAsync(artifacts, cancellationToken);
            return;
        }

        var runtime = await EnsureBakeRuntimeAsync(
            release,
            actorUserId,
            artifacts,
            scenarioOverlays,
            cancellationToken);
        if (runtime.Status == TeamLabRuntimeStatus.Failed)
        {
            await MarkFailedAsync(artifacts, runtime.LastError ?? "Scenario build runtime failed.", cancellationToken);
            throw Terminal("scenario_runtime_failed", runtime.LastError ?? "Scenario build runtime failed.");
        }
        if (runtime.Status == TeamLabRuntimeStatus.Destroyed)
        {
            await MarkFailedAsync(artifacts, "Scenario build runtime was destroyed before artifacts were committed.", cancellationToken);
            throw Terminal("scenario_runtime_destroyed", "Scenario build runtime was destroyed before artifacts were committed.");
        }
        if (runtime.Status != TeamLabRuntimeStatus.Running)
            throw Deferred(
                "scenario-baking",
                "scenario_runtime_in_progress",
                $"Scenario build runtime is {runtime.Status}.");

        try
        {
            foreach (var asset in bakeAssets)
            {
                var artifact = artifacts.Single(item => string.Equals(item.AssetKey, asset.Key, StringComparison.Ordinal));
                if (IsReady(artifact)) continue;
                var runtimeAsset = runtime.Assets.SingleOrDefault(item =>
                    item.Generation == runtime.Generation &&
                    string.Equals(item.TopologyKey, asset.Key, StringComparison.Ordinal));
                if (runtimeAsset?.WorkerNodeId is not { } workerNodeId ||
                    string.IsNullOrWhiteSpace(runtimeAsset.RuntimeResourceId))
                    throw Terminal(
                        "scenario_runtime_asset_missing",
                        $"Scenario runtime asset '{asset.Key}' has no committed VM identity.");
                var source = sourceTemplates[asset.ImageTemplateId];
                await CommitArtifactAsync(
                    release,
                    artifact,
                    runtimeAsset,
                    source,
                    workerNodeId,
                    actorUserId,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiOperationTerminalException exception)
        {
            await FailAndCleanupAsync(runtime, artifacts, exception.Message, cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scenario artifact commit failed for release {ReleaseId}", release.Id);
            await FailAndCleanupAsync(runtime, artifacts, exception.Message, cancellationToken);
            throw Terminal("scenario_artifact_commit_failed", exception.Message);
        }

        var cleanupResult = await cleanup.CleanupAsync(runtime, markDestroyedOnSuccess: true, cancellationToken);
        if (!cleanupResult.Success)
            throw Deferred("scenario-cleanup", "scenario_cleanup_pending", cleanupResult.Message);
    }

    private async Task<List<TeamLabReleaseAssetArtifact>> EnsureArtifactRowsAsync(
        TeamLabTopologyRelease release,
        IReadOnlyList<TeamLabExecutionAsset> bakeAssets,
        IReadOnlyDictionary<int, ImageTemplate> sourceTemplates,
        CancellationToken cancellationToken)
    {
        var existing = await context.TeamLabReleaseAssetArtifacts
            .Include(item => item.ScenarioImageTemplate)
            .Where(item => item.ReleaseId == release.Id)
            .ToListAsync(cancellationToken);
        foreach (var asset in bakeAssets)
        {
            var source = sourceTemplates[asset.ImageTemplateId];
            var identity = BuildIdentity(release.ContentHash, asset.Key, source);
            var artifact = existing.SingleOrDefault(item =>
                string.Equals(item.AssetKey, asset.Key, StringComparison.Ordinal));
            if (artifact is not null)
            {
                if (!string.Equals(artifact.BuildIdentity, identity, StringComparison.Ordinal))
                    throw Terminal(
                        "scenario_build_identity_changed",
                        $"Scenario build identity changed for immutable release asset '{asset.Key}'.");
                continue;
            }

            var reusable = await context.TeamLabReleaseAssetArtifacts.AsNoTracking()
                .Where(item => item.BuildIdentity == identity &&
                               item.Status == TeamLabReleaseArtifactStatus.Ready &&
                               item.ScenarioImageTemplateId != null)
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            artifact = new TeamLabReleaseAssetArtifact
            {
                ReleaseId = release.Id,
                AssetKey = asset.Key,
                SourceImageTemplateId = source.Id,
                CommitOperationId = StableOperationId(identity),
                BuildIdentity = identity
            };
            if (reusable is not null)
            {
                artifact.ScenarioImageTemplateId = reusable.ScenarioImageTemplateId;
                artifact.Status = TeamLabReleaseArtifactStatus.Ready;
                artifact.ArtifactDigest = reusable.ArtifactDigest;
                artifact.EvidenceDigest = reusable.EvidenceDigest;
                artifact.ArtifactSize = reusable.ArtifactSize;
                artifact.RegistryAddress = reusable.RegistryAddress;
                artifact.RegistryRepository = reusable.RegistryRepository;
                artifact.RegistryTag = reusable.RegistryTag;
                artifact.ReadyAt = reusable.ReadyAt ?? DateTimeOffset.UtcNow;
            }
            context.TeamLabReleaseAssetArtifacts.Add(artifact);
            existing.Add(artifact);
        }
        await context.SaveChangesAsync(cancellationToken);
        return existing.Where(item => bakeAssets.Any(asset =>
                string.Equals(asset.Key, item.AssetKey, StringComparison.Ordinal)))
            .OrderBy(item => item.AssetKey, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<TeamLabRuntime> EnsureBakeRuntimeAsync(
        TeamLabTopologyRelease release,
        Guid actorUserId,
        IReadOnlyList<TeamLabReleaseAssetArtifact> artifacts,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? scenarioOverlays,
        CancellationToken cancellationToken)
    {
        var runtimeIds = artifacts.Where(item => item.BakeRuntimeId.HasValue)
            .Select(item => item.BakeRuntimeId!.Value).Distinct().ToArray();
        if (runtimeIds.Length > 1)
            throw Terminal("scenario_runtime_identity_conflict", "Scenario artifacts reference multiple build runtimes.");
        int runtimeId;
        if (runtimeIds.Length == 0)
        {
            var result = await planner.CreateScenarioBuildAsync(
                release.Id,
                actorUserId,
                $"scenario-bake:{release.Id:D}",
                $"sha256:{release.ContentHash}",
                scenarioOverlays,
                cancellationToken);
            runtimeId = result.RuntimeId;
            foreach (var artifact in artifacts) artifact.BakeRuntimeId = runtimeId;
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            runtimeId = runtimeIds[0];
        }

        var runtime = await RuntimeQuery().SingleOrDefaultAsync(item => item.Id == runtimeId, cancellationToken)
                      ?? throw Terminal("scenario_runtime_missing", "Scenario build runtime no longer exists.");
        if (!runtime.IsScenarioBuild || runtime.TopologyReleaseId != release.Id)
            throw Terminal("scenario_runtime_identity_conflict", "Scenario build runtime identity is invalid.");
        if (runtime.Status is TeamLabRuntimeStatus.Failed or TeamLabRuntimeStatus.Destroyed &&
            artifacts.Any(item => !IsReady(item)))
        {
            foreach (var artifact in artifacts.Where(item => !IsReady(item)))
            {
                artifact.BakeRuntimeId = null;
                artifact.Status = TeamLabReleaseArtifactStatus.Baking;
                artifact.ErrorMessage = null;
            }
            await context.SaveChangesAsync(cancellationToken);
            var replacement = await planner.CreateScenarioBuildAsync(
                release.Id,
                actorUserId,
                $"scenario-bake:{release.Id:D}",
                $"sha256:{release.ContentHash}",
                scenarioOverlays,
                cancellationToken);
            runtimeId = replacement.RuntimeId;
            foreach (var artifact in artifacts) artifact.BakeRuntimeId = runtimeId;
            await context.SaveChangesAsync(cancellationToken);
            runtime = await RuntimeQuery().SingleAsync(item => item.Id == runtimeId, cancellationToken);
        }

        var hasTicket = await context.DeploymentQueueTickets.AsNoTracking().AnyAsync(item =>
            item.TeamLabRuntimeId == runtime.Id &&
            item.Generation == runtime.Generation &&
            item.Operation == RuntimeOperationKind.Create,
            cancellationToken);
        if (!hasTicket && runtime.Status == TeamLabRuntimeStatus.Scheduled)
        {
            var dockerSlots = runtime.Assets.Count(item =>
                item.Generation == runtime.Generation && item.Kind == TeamLabResourceKind.Docker);
            var vmSlots = runtime.Assets.Count(item =>
                item.Generation == runtime.Generation && item.Kind == TeamLabResourceKind.Vm);
            await queue.EnqueueAsync(new TeamLabQueueRequest(
                runtime.Id,
                dockerSlots,
                vmSlots,
                actorUserId,
                null,
                runtime.PublicId,
                WorkloadSchedulingIdentity.ForRuntime(runtime.Id, $"teamlab-runtime:{runtime.Id}", actorUserId),
                $"Scenario bake {release.Id:D}",
                $"{dockerSlots} Docker / {vmSlots} VM scenario build"), cancellationToken);
        }
        return runtime;
    }

    internal static IReadOnlyList<TeamLabRuntimeOverlayModel>? ValidateScenarioOverlays(
        IReadOnlyList<TeamLabExecutionAsset> bakeAssets,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? overlays)
    {
        if (overlays is null || overlays.Count == 0) return null;
        var bakeKeys = bakeAssets.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var normalized = overlays.OrderBy(item => item.AssetKey, StringComparer.Ordinal).ToArray();
        if (normalized.Select(item => item.AssetKey).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw Terminal("scenario_overlay_duplicate", "Scenario overlays contain duplicate asset keys.");
        foreach (var overlay in normalized)
        {
            if (!bakeKeys.Contains(overlay.AssetKey))
                throw Terminal(
                    "scenario_overlay_asset_invalid",
                    $"Scenario overlay asset '{overlay.AssetKey}' is not marked BakeAtPublish.");
            if (overlay.Environment is { Count: > 0 })
                throw Terminal(
                    "scenario_overlay_environment_forbidden",
                    "Scenario publication accepts protected secrets only; environment belongs in the topology definition.");
        }
        return normalized;
    }

    private async Task CommitArtifactAsync(
        TeamLabTopologyRelease release,
        TeamLabReleaseAssetArtifact artifact,
        TeamLabRuntimeAsset runtimeAsset,
        ImageTemplate source,
        Guid workerNodeId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artifact.ArtifactDigest))
        {
            var (repository, tag) = BuildRegistryTarget(release.Id, artifact.AssetKey, artifact.BuildIdentity);
            var response = await executor.CommitScenarioArtifactAsync(
                workerNodeId,
                new TeamLabScenarioArtifactCommitRequest(
                    artifact.CommitOperationId,
                    runtimeAsset.RuntimeResourceId!,
                    source.OSType,
                    artifact.BuildIdentity,
                    _registry.NormalizedAddress,
                    repository,
                    tag),
                cancellationToken);
            var artifactDigest = NormalizeDigest(response.ArtifactDigest);
            var evidenceDigest = NormalizeDigest(response.EvidenceDigest);
            if (!response.Success || artifactDigest is null || evidenceDigest is null || response.ArtifactSize <= 0)
                throw Terminal(
                    response.ErrorCode ?? "scenario_artifact_result_invalid",
                    response.ErrorDetail ?? $"Scenario artifact result for '{artifact.AssetKey}' is invalid.");
            artifact.ArtifactDigest = artifactDigest;
            artifact.EvidenceDigest = evidenceDigest;
            artifact.ArtifactSize = response.ArtifactSize;
            artifact.RegistryAddress = response.RegistryAddress;
            artifact.RegistryRepository = response.RegistryRepository;
            artifact.RegistryTag = response.RegistryTag;
            await context.SaveChangesAsync(cancellationToken);
        }

        if (artifact.ScenarioImageTemplateId is null)
        {
            var sourceCertification = source.CapabilityCertifications.First(certification =>
                BootstrapProfileCompatibilityService.IsCurrentManagedCertification(certification, source));
            var template = new ImageTemplate
            {
                Name = $"{source.Name} / scenario {release.Version} / {artifact.AssetKey}",
                OSType = source.OSType,
                ImageType = ImageType.Qcow2,
                RegistryUrl = $"oci://{artifact.RegistryAddress}/{artifact.RegistryRepository}:{artifact.RegistryTag}",
                FileSize = artifact.ArtifactSize,
                UploadedAt = DateTimeOffset.UtcNow,
                Status = ImageStatus.Ready,
                Description = $"Immutable TeamLab scenario artifact for release {release.Id:D}, asset {artifact.AssetKey}.",
                ImageHash = artifact.ArtifactDigest,
                OriginalArchiveName = source.OriginalArchiveName,
                CreatedById = actorUserId,
                VmArtifactStatus = VmArtifactStatus.Ready,
                VmRuntimeMode = VmRuntimeMode.Scenario,
                VmNetworkMode = source.VmNetworkMode
            };
            template.CapabilityCertifications.Add(new ImageTemplateCapabilityCertification
            {
                ImageTemplate = template,
                ImageHash = artifact.ArtifactDigest,
                Status = ImageTemplateCertificationStatus.Certified,
                CapabilitiesJson = sourceCertification.CapabilitiesJson,
                EvidenceDigest = artifact.EvidenceDigest,
                ProbeKind = "scenario-bake",
                ProbeStep = "scenario-artifact-committed",
                WorkerNodeId = workerNodeId,
                PreparationContractVersion = sourceCertification.PreparationContractVersion,
                GuestProtocolVersion = sourceCertification.GuestProtocolVersion,
                CertifiedById = actorUserId
            });
            context.ImageTemplates.Add(template);
            artifact.ScenarioImageTemplate = template;
            await context.SaveChangesAsync(cancellationToken);
        }

        artifact.Status = TeamLabReleaseArtifactStatus.Ready;
        artifact.ErrorMessage = null;
        artifact.ReadyAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureRuntimeCleanedAsync(
        IReadOnlyList<TeamLabReleaseAssetArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        var runtimeId = artifacts.Select(item => item.BakeRuntimeId).FirstOrDefault(item => item.HasValue);
        if (runtimeId is null) return;
        var runtime = await RuntimeQuery().SingleOrDefaultAsync(item => item.Id == runtimeId.Value, cancellationToken);
        if (runtime is null || runtime.Status == TeamLabRuntimeStatus.Destroyed) return;
        var result = await cleanup.CleanupAsync(runtime, markDestroyedOnSuccess: true, cancellationToken);
        if (!result.Success)
            throw Deferred("scenario-cleanup", "scenario_cleanup_pending", result.Message);
    }

    private async Task FailAndCleanupAsync(
        TeamLabRuntime runtime,
        IReadOnlyList<TeamLabReleaseAssetArtifact> artifacts,
        string error,
        CancellationToken cancellationToken)
    {
        await MarkFailedAsync(artifacts, error, cancellationToken);
        var result = await cleanup.CleanupAsync(runtime, markDestroyedOnSuccess: true, cancellationToken);
        if (!result.Success)
            logger.LogError(
                "Scenario runtime {RuntimeId} cleanup failed after artifact error: {CleanupError}",
                runtime.PublicId,
                result.Message);
    }

    private async Task MarkFailedAsync(
        IEnumerable<TeamLabReleaseAssetArtifact> artifacts,
        string error,
        CancellationToken cancellationToken)
    {
        foreach (var artifact in artifacts.Where(item => item.Status != TeamLabReleaseArtifactStatus.Ready))
        {
            artifact.Status = TeamLabReleaseArtifactStatus.Failed;
            artifact.ErrorMessage = Trim(error);
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<TeamLabRuntime> RuntimeQuery() => context.TeamLabRuntimes
        .Include(item => item.PublicUdpMapping)
        .Include(item => item.Shards).ThenInclude(item => item.Networks)
        .Include(item => item.Shards).ThenInclude(item => item.Assets)
        .Include(item => item.Networks).ThenInclude(item => item.NetworkLease)
        .Include(item => item.Assets)
        .Include(item => item.Infrastructure).ThenInclude(item => item.Fragments)
        .Include(item => item.DependencyStates)
        .Include(item => item.BootstrapExecutions)
        .Include(item => item.ObservationPoints)
        .Include(item => item.FabricLinkLeases)
        .Include(item => item.AccessGrants)
        .Include(item => item.VpnPeers)
        .Include(item => item.SecretEnvelopes)
        .Include(item => item.TrafficCaptureJobs).ThenInclude(item => item.Segments)
        .Include(item => item.Events);

    private static void ValidateSources(
        IReadOnlyList<TeamLabExecutionAsset> assets,
        IReadOnlyDictionary<int, ImageTemplate> templates)
    {
        foreach (var asset in assets)
        {
            if (!templates.TryGetValue(asset.ImageTemplateId, out var template) ||
                template.Status != ImageStatus.Ready ||
                template.ImageType == ImageType.Docker ||
                template.VmRuntimeMode != VmRuntimeMode.Managed ||
                template.VmArtifactStatus != VmArtifactStatus.Ready ||
                string.IsNullOrWhiteSpace(template.ImageHash) ||
                !template.CapabilityCertifications.Any(certification =>
                    BootstrapProfileCompatibilityService.IsCurrentManagedCertification(certification, template)))
                throw Terminal(
                    "scenario_source_not_managed",
                    $"BakeAtPublish asset '{asset.Key}' requires a ready, certified Managed VM template.");
        }
    }

    private (string Repository, string Tag) BuildRegistryTarget(Guid releaseId, string assetKey, string identity)
    {
        var path = $"gzctf/teamlab-scenario/{releaseId:N}";
        var repository = string.IsNullOrWhiteSpace(_registry.NormalizedNamespace)
            ? path
            : $"{_registry.NormalizedNamespace}/{path}";
        var safeAssetKey = new string(assetKey.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '-')
            .ToArray());
        return (repository, $"{safeAssetKey}-{identity[..16]}");
    }

    private static string BuildIdentity(string releaseHash, string assetKey, ImageTemplate source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{releaseHash}:{assetKey}:{source.Id}:{source.ImageHash}:{source.VmNetworkMode}")));

    private static Guid StableOperationId(string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"teamlab-scenario:{identity}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static bool IsReady(TeamLabReleaseAssetArtifact artifact) =>
        artifact.Status == TeamLabReleaseArtifactStatus.Ready &&
        artifact.ScenarioImageTemplateId.HasValue &&
        !string.IsNullOrWhiteSpace(artifact.ArtifactDigest);

    private static string? NormalizeDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim().ToLowerInvariant();
        if (value.StartsWith("sha256:", StringComparison.Ordinal)) value = value[7..];
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value : null;
    }

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];

    private static ApiOperationDeferredException Deferred(string stage, string code, string message) =>
        new(stage, code, message, StatePollInterval);

    private static ApiOperationTerminalException Terminal(string code, string message) => new(code, message);
}
