using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabTopologyApplicationService(
    AppDbContext context,
    TeamLabTopologyValidator validator,
    TeamLabReleaseService releases) : ITeamLabTopologyApplicationService
{
    public TeamLabCapabilitiesModel GetCapabilities() => new(
        "v1",
        [1],
        [TeamLabAssetKind.Docker, TeamLabAssetKind.Vm],
        "L3RoutedFabric",
        new TeamLabFeatureCapabilitiesModel(
            MultiNode: true,
            LinuxVm: true,
            WindowsVm: false,
            TrafficFlows: true,
            OnDemandPcap: true),
        new TeamLabContractLimitsModel(
            TeamLabTopologyValidator.MaxNetworks,
            TeamLabTopologyValidator.MaxAssets,
            TeamLabTopologyValidator.MaxInterfacesPerAsset));

    public async Task<TeamLabTopologyDetailModel> CreateAsync(
        CreateTeamLabTopologyModel model,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var definition = TeamLabReleaseCodec.Normalize(new TeamLabTopologyDefinitionModel(
            model.Name, model.Networks, model.Assets, model.Connections));
        await RequireValidAsync(definition, cancellationToken);
        var topology = BuildTopology(definition, actorUserId);
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
                item.PublicId, item.Name, item.Revision, item.SchemaVersion, item.CreatedAt, item.UpdatedAt))
            .ToArrayAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var definition = TeamLabReleaseCodec.Normalize(new TeamLabTopologyDefinitionModel(
            model.Name, model.Networks, model.Assets, model.Connections));
        await RequireValidAsync(definition, cancellationToken);
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
                .SetProperty(item => item.EditorMetadataJson, SerializeEditor(NormalizeEditor(model.Editor, definition)))
                .SetProperty(item => item.Revision, item => item.Revision + 1)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        if (updated == 0)
            throw new TeamLabApiContractException(
                "topology_revision_conflict",
                $"Topology revision is {identity.Revision}, not {model.Revision}.",
                409);

        await context.TeamLabTopologyConnections.Where(item => item.TopologyId == identity.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await context.TeamLabTopologyInterfaces.Where(item => item.Asset.TopologyId == identity.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await context.TeamLabTopologyAssets.Where(item => item.TopologyId == identity.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await context.TeamLabTopologyNetworks.Where(item => item.TopologyId == identity.Id)
            .ExecuteDeleteAsync(cancellationToken);
        AddDefinitionChildren(identity.Id, definition);
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
        var result = validator.Validate(definition);
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
        return await releases.PublishAsync(topology, revision, actorUserId, cancellationToken);
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
            ?? throw new TeamLabApiContractException("release_not_found", "The topology release was not found.", 404);
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
            ?? throw new TeamLabApiContractException("release_not_found", "The topology release was not found.", 404);
        var now = DateTimeOffset.UtcNow;
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(item => item.IsSchedulable && item.TeamLabNetworkEnabled &&
                           item.TeamLabTunnelStatus == TeamLabTunnelStatus.Healthy &&
                           item.Status == NodeStatus.Online &&
                           (item.IsLocal || item.LastHeartbeat >= now - WorkerNode.DefaultHeartbeatTimeout))
            .Select(item => new TeamLabPlanningNodeSnapshot(
                item.Id,
                item.Name,
                (item.Capabilities & NodeCapability.Docker) != 0,
                (item.Capabilities & NodeCapability.Kvm) != 0,
                item.MaxContainers - item.CurrentContainers - item.ReservedContainers,
                item.MaxVms - item.CurrentVms - item.ReservedVms,
                item.CpuLoad,
                item.MemoryLoad))
            .ToArrayAsync(cancellationToken);
        return TeamLabAssetPlanner.Build(topology.PublicId, release.Id, TeamLabReleaseCodec.Decode(release.CanonicalJson), nodes);
    }

    internal static TeamLabApiContractException InvalidTopology(TeamLabValidationResultModel result) =>
        new("topology_invalid", string.Join("; ", result.Issues.Select(item => $"{item.Path}: {item.Message}")), 422);

    internal static async Task ValidateImageTemplatesAsync(
        AppDbContext context,
        TeamLabTopologyDefinitionModel definition,
        CancellationToken cancellationToken)
    {
        var requested = definition.Assets.Select(item => item.ImageTemplateId).Distinct().ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => requested.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var asset in definition.Assets)
        {
            if (!templates.TryGetValue(asset.ImageTemplateId, out var template) || template.Status != ImageStatus.Ready)
                throw new TeamLabApiContractException(
                    "image_template_unavailable",
                    $"Image template {asset.ImageTemplateId} is not ready.",
                    422);
            var kindMatches = asset.Kind == TeamLabAssetKind.Docker
                ? template.ImageType == ImageType.Docker
                : template.ImageType != ImageType.Docker;
            if (!kindMatches)
                throw new TeamLabApiContractException(
                    "image_template_unavailable",
                    $"Image template {asset.ImageTemplateId} does not match asset kind {asset.Kind}.",
                    422);
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
                    item.RoutingEnabled,
                    item.ExposePort,
                    DeserializeEnvironment(item.EnvironmentJson),
                    item.StartCommand,
                    item.HealthCheckKind is { } kind && item.HealthCheckPort is { } port
                        ? new TeamLabHealthCheckModel(kind, port)
                        : null,
                    item.OrderIndex)).ToArray(),
            topology.Connections.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new TeamLabTopologyConnectionModel(
                    item.Key, item.FromNetworkKey, item.ToNetworkKey, item.ViaAssetKey)).ToArray());

    private async Task RequireValidAsync(TeamLabTopologyDefinitionModel definition, CancellationToken cancellationToken)
    {
        var validation = validator.Validate(definition);
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

    private static TeamLabTopology BuildTopology(TeamLabTopologyDefinitionModel definition, Guid ownerUserId)
    {
        var topology = new TeamLabTopology
        {
            Name = definition.Name,
            OwnerUserId = ownerUserId,
            Revision = 1
        };
        AddDefinitionChildren(topology, definition);
        return topology;
    }

    private void AddDefinitionChildren(int topologyId, TeamLabTopologyDefinitionModel definition)
    {
        var topology = new TeamLabTopology { Id = topologyId };
        AddDefinitionChildren(topology, definition);
        foreach (var network in topology.Networks) network.Topology = null!;
        foreach (var asset in topology.Assets) asset.Topology = null!;
        foreach (var connection in topology.Connections) connection.Topology = null!;
        context.TeamLabTopologyNetworks.AddRange(topology.Networks);
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
        foreach (var model in definition.Assets)
        {
            var asset = new TeamLabTopologyAsset
            {
                Topology = topology,
                TopologyId = topology.Id,
                Key = model.Key,
                Name = model.Name,
                Kind = model.Kind,
                ImageTemplateId = model.ImageTemplateId,
                CpuUnits = model.Resources.CpuUnits,
                MemoryMiB = model.Resources.MemoryMiB,
                StorageMiB = model.Resources.StorageMiB,
                ExposePort = model.ExposePort,
                RoutingEnabled = model.RoutingEnabled,
                EnvironmentJson = JsonSerializer.Serialize(model.Environment ?? new Dictionary<string, string>()),
                StartCommand = model.StartCommand,
                HealthCheckKind = model.HealthCheck?.Kind,
                HealthCheckPort = model.HealthCheck?.Port,
                OrderIndex = model.OrderIndex
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
                ViaAssetKey = model.ViaAssetKey
            });
        }
    }

    private static TeamLabTopologyDetailModel ToDetail(TeamLabTopology topology) =>
        new(topology.PublicId, topology.Revision, topology.SchemaVersion, ToDefinition(topology),
            DeserializeEditor(topology.EditorMetadataJson), topology.CreatedAt, topology.UpdatedAt);

    private static TeamLabTopologyEditorModel NormalizeEditor(
        TeamLabTopologyEditorModel? editor,
        TeamLabTopologyDefinitionModel definition)
    {
        var networkKeys = definition.Networks.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var assetKeys = definition.Assets.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        return new TeamLabTopologyEditorModel(
            NormalizeEditorItems(editor?.Networks, networkKeys),
            NormalizeEditorItems(editor?.Assets, assetKeys));
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

    private static TeamLabTopologyEditorModel DeserializeEditor(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TeamLabTopologyEditorModel>(json)
                   ?? new TeamLabTopologyEditorModel(new Dictionary<string, TeamLabEditorItemModel>(),
                       new Dictionary<string, TeamLabEditorItemModel>());
        }
        catch (JsonException)
        {
            return new TeamLabTopologyEditorModel(new Dictionary<string, TeamLabEditorItemModel>(),
                new Dictionary<string, TeamLabEditorItemModel>());
        }
    }

    private static IReadOnlyDictionary<string, string> DeserializeEnvironment(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static TeamLabApiContractException NotFound() =>
        new("topology_not_found", "The TeamLab topology was not found.", 404);

    private sealed record TeamLabTopologyIdentity(int Id, Guid PublicId);
}
