using System.Data;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabReleaseService(
    AppDbContext context,
    TeamLabTopologyValidator validator,
    TeamLabReleaseImagePreparationService? preparation = null,
    TeamLabRuntimeOperationPayloadProtector? operationPayloads = null)
{
    private static readonly JsonSerializerOptions EditorJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TeamLabReleaseModel> PublishAsync(
        TeamLabTopology topology,
        int expectedRevision,
        Guid actorUserId,
        Guid? operationId,
        CancellationToken cancellationToken)
    {
        if (operationId is { } operation)
        {
            var applied = await context.TeamLabTopologyReleases.AsNoTracking()
                .SingleOrDefaultAsync(item => item.ApiOperationId == operation, cancellationToken);
            if (applied is not null)
                return ToModel(applied, topology.PublicId);
        }
        if (topology.Revision != expectedRevision)
            throw new TeamLabApiContractException(
                "topology_revision_conflict",
                $"拓扑修订号为 {topology.Revision}，而非 {expectedRevision}",
                409);

        var definition = TeamLabTopologyApplicationService.ToDefinition(topology);
        var validation = validator.Validate(definition, topology.SchemaVersion);
        if (!validation.Valid)
            throw TeamLabTopologyApplicationService.InvalidTopology(validation);
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, definition, cancellationToken);
        await TeamLabTopologyApplicationService.ValidateCapabilityResourcesAsync(context, definition, cancellationToken);
        var vmTemplateIds = definition.Assets.Where(asset => asset.Kind == TeamLabAssetKind.Vm)
            .Select(asset => asset.ImageTemplateId).Distinct().ToArray();
        var networkModes = await context.ImageTemplates.AsNoTracking()
            .Where(template => vmTemplateIds.Contains(template.Id))
            .ToDictionaryAsync(template => template.Id, template => template.VmNetworkMode, cancellationToken);
        definition = definition with
        {
            Assets = definition.Assets.Select(asset => asset.Kind == TeamLabAssetKind.Vm
                ? asset with { VmNetworkMode = asset.VmNetworkMode ?? networkModes[asset.ImageTemplateId] }
                : asset).ToArray()
        };
        var imageDigests = await LoadImageDigestsAsync(definition, cancellationToken);
        var devicePackageDigests = await LoadDevicePackageDigestsAsync(definition, imageDigests, cancellationToken);
        var canonicalJson = TeamLabReleaseCodec.Encode(topology.SchemaVersion, definition, imageDigests, devicePackageDigests);

        var contentHash = TeamLabReleaseCodec.ComputeContentHash(topology.SchemaVersion, canonicalJson);
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        if (string.Equals(
                context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
        {
            var releaseLock = $"teamlab:topology-release:{topology.Id}";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({releaseLock}, 0))",
                cancellationToken);
        }
        var persistedRevision = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.Id == topology.Id)
            .Select(item => (int?)item.Revision)
            .SingleOrDefaultAsync(cancellationToken);
        if (persistedRevision != expectedRevision)
            throw new TeamLabApiContractException(
                "topology_revision_conflict",
                $"拓扑修订号为 {persistedRevision?.ToString() ?? "不可用"}，而非 {expectedRevision}",
                409);

        var existing = await context.TeamLabTopologyReleases
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.TopologyId == topology.Id &&
                item.SourceRevision == topology.Revision &&
                item.ContentHash == contentHash,
                cancellationToken);
        if (existing is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return ToModel(existing, topology.PublicId);
        }

        var nextVersion = (await context.TeamLabTopologyReleases
            .Where(item => item.TopologyId == topology.Id)
            .MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0) + 1;
        var release = new TeamLabTopologyRelease
        {
            TopologyId = topology.Id,
            ControlScopeId = topology.ControlScopeId,
            Version = nextVersion,
            SourceRevision = topology.Revision,
            SchemaVersion = topology.SchemaVersion,
            CanonicalJson = canonicalJson,
            EditorMetadataJson = topology.EditorMetadataJson,
            ContentHash = contentHash,
            PublishedById = actorUserId,
            ApiOperationId = operationId
        };
        context.TeamLabTopologyReleases.Add(release);
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return ToModel(release, topology.PublicId);
    }

    /// <summary>
    /// Freezes the device package digest per asset key at publish time, mirroring the
    /// image digest freeze so later registry drift is detectable at planning time.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string?>> LoadDevicePackageDigestsAsync(
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyDictionary<string, string> imageDigests,
        CancellationToken cancellationToken)
    {
        var packageIds = definition.Assets
            .Where(item => item.DevicePackageId is { } id && id > 0)
            .Select(item => item.DevicePackageId!.Value)
            .Distinct()
            .ToArray();
        var packages = await context.TeamLabDevicePackages.AsNoTracking()
            .Where(item => packageIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var asset in definition.Assets.Where(item => item.DevicePackageId.HasValue))
        {
            if (!packages.TryGetValue(asset.DevicePackageId!.Value, out var package))
                throw new TeamLabApiContractException("device_package_unavailable", "引用的设备包不可用。", 422);
            TeamLabDeviceExecutionCompiler.RequireResources(package, asset.Resources);
            await TeamLabDeviceExecutionCompiler.CompileAsync(package, asset.Kind, imageDigests[asset.Key],
                asset.DeviceParameters?.GetRawText(), cancellationToken);
        }
        return definition.Assets.ToDictionary(
            asset => asset.Key,
            asset => asset.DevicePackageId is { } id && id > 0
                ? packages.GetValueOrDefault(id)?.Digest
                  ?? throw new TeamLabApiContractException(
                      "device_package_unavailable", $"资产 '{asset.Key}' 引用的设备包不可用", 422)
                : null,
            StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadImageDigestsAsync(
        TeamLabTopologyDefinitionModel definition,
        CancellationToken cancellationToken)
    {
        var templateIds = definition.Assets.Select(item => item.ImageTemplateId).Distinct().ToArray();
        var digests = await context.ImageTemplates.AsNoTracking()
            .Where(item => templateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.ImageHash, cancellationToken);
        return definition.Assets.ToDictionary(
            asset => asset.Key,
            asset => digests.GetValueOrDefault(asset.ImageTemplateId)
                     ?? throw new TeamLabApiContractException(
                         "image_template_unavailable", $"资产 '{asset.Key}' 的镜像模板不可用", 422),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Archives a release: history stays readable, active runtimes keep running,
    /// but no new runtimes may be planned from it. Idempotent.
    /// </summary>
    public async Task ArchiveAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        await using (var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null)
        {
            await TeamLabReleaseLifecycle.LockAsync(context, releaseId, cancellationToken);
            var release = await context.TeamLabTopologyReleases
                .SingleOrDefaultAsync(item => item.Id == releaseId, cancellationToken)
                ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
            if (context.Database.IsRelational())
                await context.Entry(release).ReloadAsync(cancellationToken);
            if (!release.IsArchived)
            {
                await TeamLabReleaseLifecycle.RequireNoActiveResetAsync(context, releaseId, operationPayloads, cancellationToken);
                release.IsArchived = true;
                release.ArchivedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        // Retry the tail even for an already archived version. Its immutable definition stays
        // readable; only this release's prewarm demand is withdrawn, never runtime consumers.
        if (preparation is not null)
            await preparation.ReleaseAsync(releaseId, cancellationToken);
    }

    public static TeamLabReleaseModel ToModel(
        TeamLabTopologyRelease release,
        Guid topologyPublicId,
        string? publisherName = null) =>
        new(release.Id, topologyPublicId, release.Version, release.SourceRevision, release.SchemaVersion,
            release.ContentHash, release.PublishedById, publisherName, release.PublishedAt,
            DeserializeEditor(release.EditorMetadataJson), release.IsArchived);

    private static TeamLabTopologyEditorModel DeserializeEditor(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TeamLabTopologyEditorModel>(json, EditorJsonOptions)
                   ?? EmptyEditor();
        }
        catch (JsonException)
        {
            return EmptyEditor();
        }
    }

    private static TeamLabTopologyEditorModel EmptyEditor() => new(
        new Dictionary<string, TeamLabEditorItemModel>(),
        new Dictionary<string, TeamLabEditorItemModel>(),
        new Dictionary<string, TeamLabEditorItemModel>());
}
