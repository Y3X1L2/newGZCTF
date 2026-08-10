using System.Text.Json;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabTopologyApplicationService(
    AppDbContext context,
    TeamLabTopologyValidator validator,
    TeamLabReleaseService releases,
    TeamLabControlScopeService controlScopes,
    NodeCapacitySnapshotService capacitySnapshots) : ITeamLabTopologyApplicationService
{
    public async Task<TeamLabTopologyStorageReference> GetStorageReferenceAsync(
        Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken)
    {
        var item = await context.TeamLabTopologies.AsNoTracking()
            .Where(topology => topology.PublicId == topologyId &&
                               (includeAll || topology.OwnerUserId == actorUserId))
            .Select(topology => new TeamLabTopologyStorageReference(
                topology.Id, topology.PublicId, topology.OwnerUserId, topology.ControlScopeId))
            .SingleOrDefaultAsync(cancellationToken);
        return item ?? throw new TeamLabApiContractException(
            "topology_not_found", "未找到 TeamLab 拓扑或无权访问。", 404);
    }

    public async Task<TeamLabTopologyStorageReference> GetStorageReferenceAsync(
        int storageId, CancellationToken cancellationToken)
    {
        var item = await context.TeamLabTopologies.AsNoTracking()
            .Where(topology => topology.Id == storageId)
            .Select(topology => new TeamLabTopologyStorageReference(
                topology.Id, topology.PublicId, topology.OwnerUserId, topology.ControlScopeId))
            .SingleOrDefaultAsync(cancellationToken);
        return item ?? throw new TeamLabApiContractException("topology_not_found", "未找到 TeamLab 拓扑。", 404);
    }

    public TeamLabCapabilitiesModel GetCapabilities() => new(
        "v1",
        [1, 2],
        [TeamLabAssetKind.Docker, TeamLabAssetKind.Vm],
        "L3RoutedFabric",
        new TeamLabFeatureCapabilitiesModel(
            MultiNode: true,
            LinuxVm: true,
            WindowsVm: true,
            TrafficFlows: true,
            OnDemandPcap: true),
        new TeamLabContractLimitsModel(
            TeamLabTopologyValidator.MaxNetworks,
            TeamLabTopologyValidator.MaxAssets,
            TeamLabTopologyValidator.MaxInterfacesPerAsset));

    public async Task<TeamLabTopologyDetailModel> CreateAsync(
        CreateTeamLabTopologyModel model,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        await CreateCoreAsync(model, actorUserId, null, true, cancellationToken);

    public async Task<TeamLabTopologyDetailModel> CreateDraftAsync(
        CreateTeamLabTopologyModel model,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        await CreateCoreAsync(model, actorUserId, null, false, cancellationToken);

    public async Task<TeamLabTopologyDetailModel> CreateForOperationAsync(
        CreateTeamLabTopologyModel model,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var existing = await context.TeamLabTopologies.AsNoTracking()
            .Include(item => item.Networks)
            .Include(item => item.Assets).ThenInclude(item => item.Interfaces).ThenInclude(item => item.Network)
            .Include(item => item.Connections)
            .SingleOrDefaultAsync(item => item.CreatedByOperationId == operationId, cancellationToken);
        return existing is not null
            ? ToDetail(existing)
            : await CreateCoreAsync(model, actorUserId, operationId, true, cancellationToken);
    }

    private async Task<TeamLabTopologyDetailModel> CreateCoreAsync(
        CreateTeamLabTopologyModel model,
        Guid actorUserId,
        Guid? operationId,
        bool requireValid,
        CancellationToken cancellationToken)
    {
        var definition = TeamLabReleaseCodec.Normalize(new TeamLabTopologyDefinitionModel(
            model.Name, model.Networks, model.Assets, model.Connections,
            model.Infrastructure, model.Dependencies, model.Observation));
        if (requireValid)
            await RequireValidAsync(definition, model.SchemaVersion, cancellationToken);
        var topology = BuildTopology(definition, model.SchemaVersion, actorUserId);
        topology.ControlScopeId = model.ControlScopeId is { } scopeId
            ? (await controlScopes.RequireWritableAsync(scopeId, cancellationToken)).Id
            : (await controlScopes.EnsurePlatformScopeAsync(cancellationToken)).Id;
        topology.CreatedByOperationId = operationId;
        topology.LastMutationOperationId = operationId;
        topology.EditorMetadataJson = SerializeEditor(NormalizeEditor(model.Editor, definition));
        context.TeamLabTopologies.Add(topology);
        await context.SaveChangesAsync(cancellationToken);
        return ToDetail(topology);
    }

    public async Task<IReadOnlyList<TeamLabTopologySummaryModel>> ListAsync(
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var query = context.TeamLabTopologies.AsNoTracking();
        if (!includeAll) query = query.Where(item => item.OwnerUserId == actorUserId);
        return await query.OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.PublicId)
            .Select(item => new TeamLabTopologySummaryModel(
                item.PublicId, item.ControlScopeId, item.Name, item.Revision, item.SchemaVersion, item.CreatedAt, item.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<OpenTeamLabTopologyPageModel> ListPageAsync(
        Guid actorUserId,
        bool includeAll,
        int limit,
        string? after,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after, "topology_cursor_invalid");
        var query = context.TeamLabTopologies.AsNoTracking();
        if (!includeAll) query = query.Where(item => item.OwnerUserId == actorUserId);
        if (cursor is { } value)
            query = query.Where(item => item.UpdatedAt < value.Time ||
                                        item.UpdatedAt == value.Time && item.PublicId.CompareTo(value.Id) > 0);
        var rows = await query
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.PublicId)
            .Take(normalizedLimit + 1)
            .Select(item => new TeamLabTopologySummaryModel(
                item.PublicId, item.ControlScopeId, item.Name, item.Revision, item.SchemaVersion, item.CreatedAt, item.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(normalizedLimit).ToArray();
        var nextCursor = rows.Length > normalizedLimit
            ? new GuidTimeCursor(page[^1].UpdatedAt, page[^1].Id).Encode()
            : null;
        return new OpenTeamLabTopologyPageModel(page, nextCursor);
    }

    public async Task<OpenTeamLabTopologyPageModel> ListPageForScopesAsync(
        IReadOnlySet<Guid> scopeIds,
        int limit,
        string? after,
        CancellationToken cancellationToken)
    {
        if (scopeIds.Count == 0) return new OpenTeamLabTopologyPageModel([], null);
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after, "topology_cursor_invalid");
        var query = context.TeamLabTopologies.AsNoTracking().Where(item =>
            item.ControlScopeId.HasValue && scopeIds.Contains(item.ControlScopeId.Value));
        if (cursor is { } value)
            query = query.Where(item => item.UpdatedAt < value.Time ||
                                        item.UpdatedAt == value.Time && item.PublicId.CompareTo(value.Id) > 0);
        var rows = await query.OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.PublicId)
            .Take(normalizedLimit + 1)
            .Select(item => new TeamLabTopologySummaryModel(
                item.PublicId, item.ControlScopeId, item.Name, item.Revision, item.SchemaVersion, item.CreatedAt, item.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(normalizedLimit).ToArray();
        var nextCursor = rows.Length > normalizedLimit
            ? new GuidTimeCursor(page[^1].UpdatedAt, page[^1].Id).Encode()
            : null;
        return new OpenTeamLabTopologyPageModel(page, nextCursor);
    }

    public async Task<TeamLabTopologyDetailModel> GetAsync(
        Guid topologyId,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken) =>
        ToDetail(await RequireTopologyAsync(topologyId, actorUserId, includeAll, cancellationToken));

    public async Task<TeamLabTopologyDetailModel> UpdateAsync(
        Guid topologyId,
        UpdateTeamLabTopologyModel model,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken) =>
        await UpdateCoreAsync(topologyId, model, actorUserId, includeAll, null, true, cancellationToken);

    public async Task<TeamLabTopologyDetailModel> UpdateDraftAsync(
        Guid topologyId,
        UpdateTeamLabTopologyModel model,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken) =>
        await UpdateCoreAsync(topologyId, model, actorUserId, includeAll, null, false, cancellationToken);

    public async Task<TeamLabTopologyDetailModel> UpdateForOperationAsync(
        Guid topologyId,
        UpdateTeamLabTopologyModel model,
        Guid actorUserId,
        bool includeAll,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var alreadyApplied = await context.TeamLabTopologies.AsNoTracking()
            .AnyAsync(item => item.PublicId == topologyId && item.LastMutationOperationId == operationId &&
                              (includeAll || item.OwnerUserId == actorUserId), cancellationToken);
        return alreadyApplied
            ? await GetAsync(topologyId, actorUserId, includeAll, cancellationToken)
            : await UpdateCoreAsync(topologyId, model, actorUserId, includeAll, operationId, true, cancellationToken);
    }

    private async Task<TeamLabTopologyDetailModel> UpdateCoreAsync(
        Guid topologyId,
        UpdateTeamLabTopologyModel model,
        Guid actorUserId,
        bool includeAll,
        Guid? operationId,
        bool requireValid,
        CancellationToken cancellationToken)
    {
        var definition = TeamLabReleaseCodec.Normalize(new TeamLabTopologyDefinitionModel(
            model.Name, model.Networks, model.Assets, model.Connections,
            model.Infrastructure, model.Dependencies, model.Observation));
        if (requireValid)
            await RequireValidAsync(definition, model.SchemaVersion, cancellationToken);
        var identity = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.PublicId == topologyId && (includeAll || item.OwnerUserId == actorUserId))
            .Select(item => new { item.Id, item.Revision })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var updated = await context.TeamLabTopologies
            .Where(item => item.Id == identity.Id && item.Revision == model.Revision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Name, definition.Name)
                .SetProperty(item => item.SchemaVersion, model.SchemaVersion)
                .SetProperty(item => item.EditorMetadataJson, SerializeEditor(NormalizeEditor(model.Editor, definition)))
                .SetProperty(item => item.InfrastructureJson, Serialize(definition.Infrastructure ?? []))
                .SetProperty(item => item.DependenciesJson, Serialize(definition.Dependencies ?? []))
                .SetProperty(item => item.ObservationJson, Serialize(definition.Observation ?? new TeamLabObservationPolicyModel()))
                .SetProperty(item => item.Revision, item => item.Revision + 1)
                .SetProperty(item => item.LastMutationOperationId, operationId)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        if (updated == 0)
            throw new TeamLabApiContractException(
                "topology_revision_conflict",
                $"拓扑修订号为 {identity.Revision}，不是 {model.Revision}",
                409);

        var currentNetworks = await context.TeamLabTopologyNetworks
            .Where(item => item.TopologyId == identity.Id)
            .ToDictionaryAsync(item => item.Key, StringComparer.Ordinal, cancellationToken);
        var requestedNetworkKeys = definition.Networks
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);
        var removedNetworks = currentNetworks.Values
            .Where(item => !requestedNetworkKeys.Contains(item.Key))
            .ToArray();

        await context.TeamLabTopologyConnections.Where(item => item.TopologyId == identity.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await context.TeamLabTopologyInterfaces.Where(item => item.Asset.TopologyId == identity.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await context.TeamLabTopologyAssets.Where(item => item.TopologyId == identity.Id)
            .ExecuteDeleteAsync(cancellationToken);
        context.TeamLabTopologyNetworks.RemoveRange(removedNetworks);
        foreach (var network in definition.Networks)
        {
            if (!currentNetworks.TryGetValue(network.Key, out var entity))
            {
                entity = new TeamLabTopologyNetwork
                {
                    TopologyId = identity.Id,
                    Key = network.Key
                };
                currentNetworks.Add(network.Key, entity);
                context.TeamLabTopologyNetworks.Add(entity);
            }

            entity.Name = network.Name;
            entity.AddressPoolCidr = network.AddressPool.PoolCidr;
            entity.RuntimePrefixLength = network.AddressPool.RuntimePrefixLength;
            entity.IsEntry = network.IsEntry;
            entity.OrderIndex = network.OrderIndex;
        }

        await context.SaveChangesAsync(cancellationToken);
        AddDefinitionChildren(
            identity.Id,
            definition,
            currentNetworks
                .Where(item => requestedNetworkKeys.Contains(item.Key))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(topologyId, actorUserId, includeAll, cancellationToken);
    }

    public async Task DeleteAsync(
        Guid topologyId,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var topology = await context.TeamLabTopologies
            .SingleOrDefaultAsync(item => item.PublicId == topologyId && (includeAll || item.OwnerUserId == actorUserId), cancellationToken)
            ?? throw NotFound();
        if (await context.TeamLabTopologyReleases.AnyAsync(item => item.TopologyId == topology.Id, cancellationToken))
            throw new TeamLabApiContractException(
                "release_immutable",
                "已发布版本的拓扑无法删除",
                409);
        context.TeamLabTopologies.Remove(topology);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteForOperationAsync(
        Guid topologyId,
        Guid actorUserId,
        bool includeAll,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        _ = operationId;
        var topology = await context.TeamLabTopologies
            .SingleOrDefaultAsync(item => item.PublicId == topologyId &&
                                          (includeAll || item.OwnerUserId == actorUserId), cancellationToken);
        if (topology is null)
            return;
        if (await context.TeamLabTopologyReleases.AnyAsync(item => item.TopologyId == topology.Id, cancellationToken))
            throw new TeamLabApiContractException(
                "release_immutable",
                "A topology with published releases cannot be deleted.",
                409);
        context.TeamLabTopologies.Remove(topology);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<TeamLabValidationResultModel> ValidateAsync(
        Guid topologyId,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var topology = await RequireTopologyAsync(topologyId, actorUserId, includeAll, cancellationToken);
        var definition = ToDefinition(topology);
        var result = validator.Validate(definition, topology.SchemaVersion);
        if (!result.Valid) return result;
        try
        {
            await ValidateImageTemplatesAsync(context, definition, cancellationToken);
            return result;
        }
        catch (TeamLabApiContractException exception)
        {
            return new TeamLabValidationResultModel(false,
                [new TeamLabValidationIssueModel(exception.Code, "assets", exception.Message)]);
        }
    }

    public async Task<TeamLabReleaseModel> PublishAsync(
        Guid topologyId,
        int revision,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var topology = await RequireTopologyAsync(topologyId, actorUserId, includeAll, cancellationToken);
        return await releases.PublishAsync(topology, revision, actorUserId, null, cancellationToken);
    }

    public async Task<TeamLabReleaseModel> PublishForOperationAsync(
        Guid topologyId,
        int revision,
        Guid actorUserId,
        bool includeAll,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var topology = await RequireTopologyAsync(topologyId, actorUserId, includeAll, cancellationToken);
        return await releases.PublishAsync(
            topology, revision, actorUserId, operationId, cancellationToken);
    }

    public async Task<IReadOnlyList<TeamLabReleaseModel>> ListReleasesAsync(
        Guid topologyId,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var topology = await RequireTopologyIdentityAsync(topologyId, actorUserId, includeAll, cancellationToken);
        var rows = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.TopologyId == topology.Id)
            .OrderByDescending(item => item.Version)
            .ToArrayAsync(cancellationToken);
        return rows.Select(item => TeamLabReleaseService.ToModel(item, topology.PublicId)).ToArray();
    }

    public async Task<OpenTeamLabReleasePageModel> ListReleasesPageAsync(
        Guid topologyId,
        Guid actorUserId,
        bool includeAll,
        int limit,
        string? after,
        CancellationToken cancellationToken)
    {
        var topology = await RequireTopologyIdentityAsync(topologyId, actorUserId, includeAll, cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after, "release_cursor_invalid");
        var query = context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.TopologyId == topology.Id);
        if (cursor is { } value)
            query = query.Where(item => item.PublishedAt < value.Time ||
                                        item.PublishedAt == value.Time && item.Id.CompareTo(value.Id) > 0);
        var rows = await query
            .OrderByDescending(item => item.PublishedAt)
            .ThenBy(item => item.Id)
            .Take(normalizedLimit + 1)
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(normalizedLimit)
            .Select(item => TeamLabReleaseService.ToModel(item, topology.PublicId).ToOpen())
            .ToArray();
        var nextCursor = rows.Length > normalizedLimit
            ? new GuidTimeCursor(page[^1].PublishedAt, page[^1].Id).Encode()
            : null;
        return new OpenTeamLabReleasePageModel(page, nextCursor);
    }

    public async Task<TeamLabReleaseModel> GetReleaseAsync(
        Guid topologyId,
        Guid releaseId,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var topology = await RequireTopologyIdentityAsync(topologyId, actorUserId, includeAll, cancellationToken);
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.TopologyId == topology.Id && item.Id == releaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到该拓扑版本", 404);
        return TeamLabReleaseService.ToModel(release, topology.PublicId);
    }

    public async Task<TeamLabPlanModel> PlanAsync(
        Guid topologyId,
        Guid releaseId,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var topology = await RequireTopologyIdentityAsync(topologyId, actorUserId, includeAll, cancellationToken);
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.TopologyId == topology.Id && item.Id == releaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到该拓扑版本", 404);
        var nodes = (await capacitySnapshots.LoadAsync(cancellationToken))
            .Where(item => item.Node.IsSchedulable && item.Node.TeamLabNetworkEnabled &&
                           item.Node.TeamLabTunnelStatus == TeamLabTunnelStatus.Healthy &&
                           item.Node.GetEffectiveStatus(DateTimeOffset.UtcNow) == NodeStatus.Online)
            .Select(item => new TeamLabPlanningNodeSnapshot(
                item.Node.Id,
                item.Node.Name,
                (item.Node.Capabilities & NodeCapability.Docker) != 0,
                (item.Node.Capabilities & NodeCapability.Kvm) != 0,
                item.AvailableDocker,
                item.AvailableVm,
                item.Node.CpuLoad,
                item.Node.MemoryLoad,
                item.Available))
            .ToArray();
        return TeamLabAssetPlanner.Build(
            topology.PublicId,
            release.Id,
            TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson),
            nodes);
    }

    internal static TeamLabApiContractException InvalidTopology(TeamLabValidationResultModel result) =>
        new("topology_invalid", string.Join("; ", result.Issues.Select(item => $"{item.Path}: {item.Message}")), 422);

    internal static async Task ValidateImageTemplatesAsync(
        AppDbContext context,
        TeamLabTopologyDefinitionModel definition,
        CancellationToken cancellationToken)
        => await ValidateImageTemplatesAsync(
            context,
            definition.Assets.Select(item => new TeamLabImageTemplateRequirement(
                item.ImageTemplateId, item.Kind, item.Key, null)).ToArray(),
            cancellationToken);

    internal static async Task ValidateImageTemplatesAsync(
        AppDbContext context,
        TeamLabExecutionTopology topology,
        CancellationToken cancellationToken)
        => await ValidateImageTemplatesAsync(
            context,
            topology.Assets.Select(item => new TeamLabImageTemplateRequirement(
                item.ImageTemplateId,
                item.Kind,
                item.Key,
                item.ImageDigest)).ToArray(),
            cancellationToken);

    private static async Task ValidateImageTemplatesAsync(
        AppDbContext context,
        IReadOnlyList<TeamLabImageTemplateRequirement> assets,
        CancellationToken cancellationToken)
    {
        var requested = assets.Select(item => item.ImageTemplateId).Distinct().ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => requested.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var asset in assets)
        {
            if (!templates.TryGetValue(asset.ImageTemplateId, out var template) || template.Status != ImageStatus.Ready)
                throw new TeamLabApiContractException(
                    "image_template_unavailable",
                    $"资产 '{asset.AssetKey}' 的镜像模板 {asset.ImageTemplateId} 尚未就绪",
                    422);
            if (string.IsNullOrWhiteSpace(template.ImageHash))
                throw new TeamLabApiContractException(
                    "image_template_unavailable",
                    $"资产 '{asset.AssetKey}' 的镜像模板 {asset.ImageTemplateId} 没有不可变摘要",
                    422);
            var kindMatches = asset.Kind == TeamLabAssetKind.Docker
                ? template.ImageType == ImageType.Docker
                : template.ImageType != ImageType.Docker;
            if (!kindMatches)
                throw new TeamLabApiContractException(
                    "image_template_unavailable",
                    $"资产 '{asset.AssetKey}' 的镜像模板 {asset.ImageTemplateId} 与资产类型 {asset.Kind} 不匹配",
                    422);
            if (!string.IsNullOrWhiteSpace(asset.ImageDigest) &&
                !string.Equals(asset.ImageDigest, template.ImageHash, StringComparison.Ordinal))
                throw new TeamLabApiContractException(
                    "image_template_digest_changed",
                    $"资产 '{asset.AssetKey}' 的镜像模板 {asset.ImageTemplateId} 不再匹配已发布摘要",
                    409);
        }
    }

    internal static TeamLabTopologyDefinitionModel ToDefinition(TeamLabTopology topology) =>
        new(
            topology.Name,
            topology.Networks.OrderBy(item => item.OrderIndex).ThenBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new TeamLabTopologyNetworkModel(
                    item.Key, item.Name, new TeamLabAddressPoolModel(item.AddressPoolCidr, item.RuntimePrefixLength),
                    item.IsEntry, item.OrderIndex)).ToArray(),
            topology.Assets.OrderBy(item => item.OrderIndex).ThenBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new TeamLabTopologyAssetModel(
                    item.Key,
                    item.Name,
                    item.Kind,
                    item.ImageTemplateId ?? 0,
                    new TeamLabAssetResourceModel(item.CpuUnits, item.MemoryMiB, item.StorageMiB),
                    item.Interfaces.OrderBy(iface => iface.OrderIndex).ThenBy(iface => iface.Key, StringComparer.Ordinal)
                        .Select(iface => new TeamLabTopologyInterfaceModel(
                            iface.Key, iface.Network.Key, iface.HostOffset, iface.IsPrimary, iface.OrderIndex)).ToArray(),
                    item.ExposePort,
                    item.HealthCheckKind is { } kind && item.HealthCheckPort is { } port
                        ? new TeamLabHealthCheckModel(kind, port)
                        : null,
                    item.OrderIndex,
                    item.EndpointObservation)).ToArray(),
            topology.Connections.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new TeamLabTopologyConnectionModel(
                    item.Key, item.FromNetworkKey, item.ToNetworkKey, item.ViaAssetKey,
                    item.ViaNodeKey, item.Direction)).ToArray(),
            DeserializeList<TeamLabTopologyInfrastructureModel>(topology.InfrastructureJson),
            DeserializeList<TeamLabTopologyDependencyModel>(topology.DependenciesJson),
            Deserialize<TeamLabObservationPolicyModel>(topology.ObservationJson));

    private async Task RequireValidAsync(
        TeamLabTopologyDefinitionModel definition,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        var validation = validator.Validate(definition, schemaVersion);
        if (!validation.Valid) throw InvalidTopology(validation);
        await ValidateImageTemplatesAsync(context, definition, cancellationToken);
    }

    private async Task<TeamLabTopology> RequireTopologyAsync(
        Guid topologyId,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken) =>
        await context.TeamLabTopologies.AsNoTracking()
            .Include(item => item.Networks)
            .Include(item => item.Assets).ThenInclude(item => item.Interfaces).ThenInclude(item => item.Network)
            .Include(item => item.Connections)
            .SingleOrDefaultAsync(item => item.PublicId == topologyId && (includeAll || item.OwnerUserId == actorUserId), cancellationToken)
        ?? throw NotFound();

    private async Task<TeamLabTopologyIdentity> RequireTopologyIdentityAsync(
        Guid topologyId,
        Guid actorUserId,
        bool includeAll,
        CancellationToken cancellationToken) =>
        await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.PublicId == topologyId && (includeAll || item.OwnerUserId == actorUserId))
            .Select(item => new TeamLabTopologyIdentity(item.Id, item.PublicId))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw NotFound();

    private static TeamLabTopology BuildTopology(
        TeamLabTopologyDefinitionModel definition,
        int schemaVersion,
        Guid ownerUserId)
    {
        var topology = new TeamLabTopology
        {
            Name = definition.Name,
            OwnerUserId = ownerUserId,
            Revision = 1,
            SchemaVersion = schemaVersion
        };
        AddDefinitionChildren(topology, definition);
        return topology;
    }

    private void AddDefinitionChildren(
        int topologyId,
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyDictionary<string, TeamLabTopologyNetwork>? existingNetworks = null)
    {
        var topology = new TeamLabTopology { Id = topologyId };
        if (existingNetworks is null)
        {
            AddDefinitionChildren(topology, definition);
            foreach (var network in topology.Networks) network.Topology = null!;
            context.TeamLabTopologyNetworks.AddRange(topology.Networks);
        }
        else
        {
            AddDefinitionAssetsAndConnections(topology, definition, existingNetworks);
        }
        foreach (var asset in topology.Assets) asset.Topology = null!;
        foreach (var connection in topology.Connections) connection.Topology = null!;
        context.TeamLabTopologyAssets.AddRange(topology.Assets);
        context.TeamLabTopologyConnections.AddRange(topology.Connections);
    }

    private static void AddDefinitionChildren(TeamLabTopology topology, TeamLabTopologyDefinitionModel definition)
    {
        var networks = definition.Networks.ToDictionary(item => item.Key, item => new TeamLabTopologyNetwork
        {
            Topology = topology,
            TopologyId = topology.Id,
            Key = item.Key,
            Name = item.Name,
            AddressPoolCidr = item.AddressPool.PoolCidr,
            RuntimePrefixLength = item.AddressPool.RuntimePrefixLength,
            IsEntry = item.IsEntry,
            OrderIndex = item.OrderIndex
        }, StringComparer.Ordinal);
        topology.Networks.AddRange(networks.Values);
        AddDefinitionAssetsAndConnections(topology, definition, networks);
    }

    private static void AddDefinitionAssetsAndConnections(
        TeamLabTopology topology,
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyDictionary<string, TeamLabTopologyNetwork> networks)
    {
        foreach (var model in definition.Assets)
        {
            var asset = new TeamLabTopologyAsset
            {
                Topology = topology,
                TopologyId = topology.Id,
                Key = model.Key,
                Name = model.Name,
                Kind = model.Kind,
                // Draft assets may be created before an image is selected. The publish validator
                // rejects that state; persisting it as null keeps the draft outside the image FK.
                ImageTemplateId = model.ImageTemplateId > 0 ? model.ImageTemplateId : null,
                CpuUnits = model.Resources.CpuUnits,
                MemoryMiB = model.Resources.MemoryMiB,
                StorageMiB = model.Resources.StorageMiB,
                ExposePort = model.ExposePort,
                HealthCheckKind = model.HealthCheck?.Kind,
                HealthCheckPort = model.HealthCheck?.Port,
                OrderIndex = model.OrderIndex,
                EndpointObservation = model.EndpointObservation
            };
            foreach (var iface in model.Interfaces)
            {
                asset.Interfaces.Add(new TeamLabTopologyInterface
                {
                    Asset = asset,
                    Network = networks[iface.NetworkKey],
                    Key = iface.Key,
                    HostOffset = iface.HostOffset,
                    IsPrimary = iface.Primary,
                    OrderIndex = iface.OrderIndex
                });
            }
            topology.Assets.Add(asset);
        }
        foreach (var model in definition.Connections)
        {
            topology.Connections.Add(new TeamLabTopologyConnection
            {
                Topology = topology,
                TopologyId = topology.Id,
                Key = model.Key,
                FromNetworkKey = model.FromNetworkKey,
                ToNetworkKey = model.ToNetworkKey,
                ViaAssetKey = model.ViaAssetKey,
                ViaNodeKey = model.ViaNodeKey,
                Direction = model.Direction ?? TeamLabConnectionDirection.Bidirectional
            });
        }
        topology.InfrastructureJson = Serialize(definition.Infrastructure ?? []);
        topology.DependenciesJson = Serialize(definition.Dependencies ?? []);
        topology.ObservationJson = Serialize(definition.Observation ?? new TeamLabObservationPolicyModel());
    }

    private static TeamLabTopologyDetailModel ToDetail(TeamLabTopology topology) =>
        new(topology.PublicId, topology.ControlScopeId, topology.Revision, topology.SchemaVersion, ToDefinition(topology),
            DeserializeEditor(topology.EditorMetadataJson), topology.CreatedAt, topology.UpdatedAt);

    private static TeamLabTopologyEditorModel NormalizeEditor(
        TeamLabTopologyEditorModel? editor,
        TeamLabTopologyDefinitionModel definition)
    {
        var networkKeys = definition.Networks.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var assetKeys = definition.Assets.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var infrastructureKeys = (definition.Infrastructure ?? [])
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);
        return new TeamLabTopologyEditorModel(
            NormalizeEditorItems(editor?.Networks, networkKeys),
            NormalizeEditorItems(editor?.Assets, assetKeys),
            NormalizeEditorItems(editor?.Infrastructure, infrastructureKeys));
    }

    private static IReadOnlyDictionary<string, TeamLabEditorItemModel> NormalizeEditorItems(
        IReadOnlyDictionary<string, TeamLabEditorItemModel>? items,
        IReadOnlySet<string> allowedKeys) =>
        (items ?? new Dictionary<string, TeamLabEditorItemModel>())
        .Where(pair => allowedKeys.Contains(pair.Key) && IsFinite(pair.Value.X) && IsFinite(pair.Value.Y))
        .ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                X = Math.Clamp(pair.Value.X, -100000, 100000),
                Y = Math.Clamp(pair.Value.Y, -100000, 100000),
                Width = NormalizeDimension(pair.Value.Width),
                Height = NormalizeDimension(pair.Value.Height)
            },
            StringComparer.Ordinal);

    private static double? NormalizeDimension(double? value) =>
        value is { } number && IsFinite(number) ? Math.Clamp(number, 80, 4000) : null;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static string SerializeEditor(TeamLabTopologyEditorModel editor) => JsonSerializer.Serialize(editor);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    private static TeamLabTopologyEditorModel DeserializeEditor(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TeamLabTopologyEditorModel>(json)
                   ?? new TeamLabTopologyEditorModel(new Dictionary<string, TeamLabEditorItemModel>(),
                       new Dictionary<string, TeamLabEditorItemModel>(),
                       new Dictionary<string, TeamLabEditorItemModel>());
        }
        catch (JsonException)
        {
            return new TeamLabTopologyEditorModel(new Dictionary<string, TeamLabEditorItemModel>(),
                new Dictionary<string, TeamLabEditorItemModel>(),
                new Dictionary<string, TeamLabEditorItemModel>());
        }
    }

    private static TeamLabApiContractException NotFound() =>
        new("topology_not_found", "未找到 TeamLab 拓扑", 404);

    private static GuidTimeCursor? DecodeCursor(string? value, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return GuidTimeCursor.Decode(value);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException(errorCode, "分页游标无效", 400);
        }
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static IReadOnlyList<T> DeserializeList<T>(string? json) =>
        Deserialize<T[]>(json) ?? [];

    private sealed record TeamLabTopologyIdentity(int Id, Guid PublicId);

    private sealed record TeamLabImageTemplateRequirement(
        int ImageTemplateId,
        TeamLabAssetKind Kind,
        string AssetKey,
        string? ImageDigest);
}
