using System.Net;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabPlanNodeResult(bool Success, string Message, WorkerNode? Node);
public sealed record TeamLabRuntimeUdpMappingModel(
    int PublicUdpPort,
    string WorkerTunnelIp,
    int WorkerWireGuardPort,
    int RuleVersion,
    bool IsSynced,
    string? LastSyncError)
{
    public static TeamLabRuntimeUdpMappingModel? FromMapping(TeamLabPublicUdpMapping? mapping) =>
        mapping is null
            ? null
            : new TeamLabRuntimeUdpMappingModel(mapping.PublicUdpPort, mapping.WorkerTunnelIp,
                mapping.WorkerWireGuardPort, mapping.RuleVersion, mapping.IsSynced, mapping.LastSyncError);
}

public sealed record TeamLabRuntimeShardModel(
    int Id,
    Guid WorkerNodeId,
    string WorkerNodeName,
    TeamLabRuntimeStatus Status,
    int RouteVersion,
    string[] NetworkKeys,
    string[] AssetKeys,
    string? LastError)
{
    public static TeamLabRuntimeShardModel FromShard(TeamLabRuntimeShard shard) =>
        new(
            shard.Id,
            shard.WorkerNodeId,
            shard.WorkerNode?.Name ?? string.Empty,
            shard.Status,
            shard.RouteVersion,
            shard.Networks.Select(network => network.TopologyKey)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray(),
            shard.Assets.Select(asset => asset.TopologyKey)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray(),
            shard.LastError);
}

public sealed record TeamLabRuntimeModel(
    int Id,
    int GameId,
    int TeamId,
    int PublishedVersion,
    Guid? WorkerNodeId,
    string NetworkPrefix,
    TeamLabRuntimeStatus Status,
    bool IsOpenToPlayers,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    TeamLabRuntimeUdpMappingModel? PublicUdpMapping,
    IReadOnlyList<TeamLabRuntimeShardModel> Shards)
{
    public static TeamLabRuntimeModel? FromRuntime(TeamLabRuntime? runtime) =>
        runtime is null
            ? null
            : new TeamLabRuntimeModel(runtime.Id, runtime.GameId, runtime.TeamId, runtime.PublishedVersion,
                runtime.WorkerNodeId, runtime.NetworkPrefix, runtime.Status, runtime.IsOpenToPlayers,
                runtime.LastError, runtime.CreatedAt, runtime.UpdatedAt,
                TeamLabRuntimeUdpMappingModel.FromMapping(runtime.PublicUdpMapping),
                runtime.Shards.Select(TeamLabRuntimeShardModel.FromShard).ToArray());
}

public sealed record TeamLabPlanResult(bool Success, string Message, [property: JsonIgnore] TeamLabRuntime? Runtime)
{
    [JsonPropertyName("runtime")]
    public TeamLabRuntimeModel? RuntimeModel => TeamLabRuntimeModel.FromRuntime(Runtime);
}

public class TeamLabPlanService(
    AppDbContext context,
    IOptions<TeamLabNetworkConfig> options,
    ILogger<TeamLabPlanService> logger)
{
    private readonly TeamLabNetworkConfig _config = options.Value;

    public static TeamLabPlanNodeResult SelectNode(IEnumerable<WorkerNode> nodes, Guid? targetNodeId = null)
    {
        var candidates = targetNodeId.HasValue
            ? nodes.Where(n => n.Id == targetNodeId.Value).ToList()
            : nodes.ToList();
        var node = WeightedScheduler.SelectOptimalTeamLabNode(candidates);
        return node is null
            ? new TeamLabPlanNodeResult(false, targetNodeId.HasValue
                ? "The WorkerNode that hosts the deployed penetration environment is not a healthy TeamLabNetwork node."
                : "No schedulable TeamLabNetwork WorkerNode is healthy.", null)
            : new TeamLabPlanNodeResult(true, "TeamLabNetwork WorkerNode selected.", node);
    }

    public static int? AllocatePublicUdpPort(int start, int end, IReadOnlySet<int> usedPorts)
    {
        if (start <= 0 || end < start || end > ushort.MaxValue)
            return null;

        for (var port = start; port <= end; port++)
        {
            if (!usedPorts.Contains(port))
                return port;
        }

        return null;
    }

    public static string BuildRuntimeResourcePrefix(int runtimeId) => $"tl{runtimeId}";

    public static bool CanReuseScheduledRuntime(TeamLabRuntime runtime, int publishedVersion) =>
        runtime.Status == TeamLabRuntimeStatus.Scheduled &&
        runtime.PublishedVersion == publishedVersion &&
        runtime.WorkerNodeId.HasValue &&
        runtime.PublicUdpMapping is not null &&
        !string.IsNullOrWhiteSpace(runtime.NetworkPrefix) &&
        runtime.Shards.Count > 0 &&
        runtime.Networks.Count > 0 &&
        runtime.Assets.Count > 0;

    public static int? ResolvePublishedVersion(PenetrationConfig? config)
    {
        if (config is null || config.PublishedVersion <= 0)
            return null;

        return config.Status is PenetrationDeploymentStatus.Published
                or PenetrationDeploymentStatus.Deploying
                or PenetrationDeploymentStatus.Running
                or PenetrationDeploymentStatus.Partial
            ? config.PublishedVersion
            : null;
    }

    public async Task<TeamLabPlanResult> PlanRuntimeAsync(int gameId, int teamId, CancellationToken token)
    {
        var penetrationConfig = await context.PenetrationConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.GameId == gameId, token);
        var publishedVersion = ResolvePublishedVersion(penetrationConfig);
        if (publishedVersion is null)
            return new TeamLabPlanResult(false, "TeamLab requires a published penetration topology version before deployment.", null);

        var runtime = await context.TeamLabRuntimes
            .Include(r => r.PublicUdpMapping)
            .Include(r => r.Shards).ThenInclude(s => s.WorkerNode)
            .Include(r => r.Networks)
            .Include(r => r.Assets)
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);

        if (runtime is not null && runtime.Status is TeamLabRuntimeStatus.Running
                or TeamLabRuntimeStatus.Deploying
                or TeamLabRuntimeStatus.Probing
                or TeamLabRuntimeStatus.Destroying)
            return new TeamLabPlanResult(false, $"TeamLab runtime is busy: {runtime.Status}.", runtime);

        if (runtime is not null && CanReuseScheduledRuntime(runtime, publishedVersion.Value))
            return new TeamLabPlanResult(true, "TeamLab runtime is already planned.", runtime);

        runtime ??= new TeamLabRuntime { GameId = gameId, TeamId = teamId };
        if (runtime.Id == 0)
        {
            context.TeamLabRuntimes.Add(runtime);
            await context.SaveChangesAsync(token);
        }

        if (!TeamLabStateMachine.CanTransition(runtime.Status, TeamLabRuntimeStatus.Planning))
            return new TeamLabPlanResult(false, $"Cannot plan TeamLab runtime from status {runtime.Status}.", runtime);

        runtime.Status = TeamLabRuntimeStatus.Planning;
        runtime.PublishedVersion = publishedVersion.Value;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;

        var topology = await LoadPublishedTopologyAsync(gameId, publishedVersion.Value, token);
        if (!topology.Success || topology.Config is null)
        {
            runtime.Status = TeamLabRuntimeStatus.Failed;
            runtime.LastError = topology.Message;
            AddEvent(runtime, "plan", TeamLabEventLevel.Error, topology.Message);
            await context.SaveChangesAsync(token);
            logger.SystemLog($"TeamLab runtime plan failed: game={gameId}, team={teamId}, runtime={runtime.Id}, error={topology.Message}",
                TaskStatus.Failed, LogLevel.Warning);
            return new TeamLabPlanResult(false, topology.Message, runtime);
        }

        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => topology.Config.Nodes.Select(n => n.ImageTemplateId).Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, token);
        var runtimeCidr = AllocateRuntimeCidr(_config.RuntimeNetworkBaseCidr, _config.TeamSubnetPrefixLength, runtime.Id);
        var assetPlan = TeamLabAssetPlanService.BuildPublishedAssetPlan(topology.Config, runtime.Id, teamIndex: 0,
            templates, runtimeCidr);
        if (!assetPlan.Success)
        {
            runtime.Status = TeamLabRuntimeStatus.Failed;
            runtime.LastError = assetPlan.Message;
            AddEvent(runtime, "plan", TeamLabEventLevel.Error, assetPlan.Message);
            await context.SaveChangesAsync(token);
            return new TeamLabPlanResult(false, assetPlan.Message, runtime);
        }

        var nodes = await context.WorkerNodes.AsNoTracking().ToListAsync(token);
        var shardPlan = TeamLabShardPlanner.PlanShards(assetPlan.Networks, assetPlan.Assets, nodes);
        if (!shardPlan.Success)
        {
            runtime.Status = TeamLabRuntimeStatus.Failed;
            runtime.LastError = shardPlan.Message;
            AddEvent(runtime, "plan", TeamLabEventLevel.Error, shardPlan.Message);
            await context.SaveChangesAsync(token);
            logger.SystemLog($"TeamLab runtime plan failed: game={gameId}, team={teamId}, runtime={runtime.Id}, error={shardPlan.Message}",
                TaskStatus.Failed, LogLevel.Warning);
            return new TeamLabPlanResult(false, shardPlan.Message, runtime);
        }

        var primaryShard = shardPlan.Shards
            .OrderBy(shard => shard.NetworkKeys.Contains("entry", StringComparer.Ordinal) ? 0 : 1)
            .ThenBy(shard => shard.WorkerNodeName, StringComparer.Ordinal)
            .First();
        var primaryNode = nodes.Single(node => node.Id == primaryShard.WorkerNodeId);

        var usedPublicPorts = await context.TeamLabPublicUdpMappings.AsNoTracking()
            .Where(m => m.RuntimeId != runtime.Id)
            .Select(m => m.PublicUdpPort)
            .ToHashSetAsync(token);
        var publicPort = AllocatePublicUdpPort(_config.PublicUdpPortStart, _config.PublicUdpPortEnd, usedPublicPorts);
        if (publicPort is null)
        {
            const string message = "No public UDP port is available for TeamLab runtime.";
            runtime.Status = TeamLabRuntimeStatus.Failed;
            runtime.LastError = message;
            AddEvent(runtime, "plan", TeamLabEventLevel.Error, message);
            await context.SaveChangesAsync(token);
            logger.SystemLog($"TeamLab runtime plan failed: game={gameId}, team={teamId}, runtime={runtime.Id}, error={message}",
                TaskStatus.Failed, LogLevel.Warning);
            return new TeamLabPlanResult(false, message, runtime);
        }

        var usedWorkerPorts = await context.TeamLabPublicUdpMappings.AsNoTracking()
            .Where(m => m.RuntimeId != runtime.Id)
            .Select(m => m.WorkerWireGuardPort)
            .ToHashSetAsync(token);
        var workerPort = AllocatePublicUdpPort(_config.WorkerWireGuardPortStart, _config.WorkerWireGuardPortEnd, usedWorkerPorts);
        if (workerPort is null)
        {
            const string message = "No WorkerNode WireGuard port is available for TeamLab runtime.";
            runtime.Status = TeamLabRuntimeStatus.Failed;
            runtime.LastError = message;
            AddEvent(runtime, "plan", TeamLabEventLevel.Error, message);
            await context.SaveChangesAsync(token);
            logger.SystemLog($"TeamLab runtime plan failed: game={gameId}, team={teamId}, runtime={runtime.Id}, error={message}",
                TaskStatus.Failed, LogLevel.Warning);
            return new TeamLabPlanResult(false, message, runtime);
        }

        runtime.WorkerNodeId = primaryNode.Id;
        runtime.NetworkPrefix = runtimeCidr;
        runtime.LastError = null;
        runtime.Status = TeamLabRuntimeStatus.Scheduled;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;

        if (runtime.PublicUdpMapping is null)
        {
            runtime.PublicUdpMapping = new TeamLabPublicUdpMapping { RuntimeId = runtime.Id };
            context.TeamLabPublicUdpMappings.Add(runtime.PublicUdpMapping);
        }

        runtime.PublicUdpMapping.PublicUdpPort = publicPort.Value;
        runtime.PublicUdpMapping.WorkerWireGuardPort = workerPort.Value;
        runtime.PublicUdpMapping.WorkerTunnelIp = primaryNode.TeamLabTunnelIp ?? string.Empty;
        runtime.PublicUdpMapping.IsSynced = false;

        ApplyShardPlan(runtime, shardPlan, assetPlan);

        AddEvent(runtime, "plan", TeamLabEventLevel.Success,
            $"TeamLab runtime planned across {shardPlan.Shards.Count} shard(s) with UDP {publicPort.Value}.");
        await context.SaveChangesAsync(token);

        logger.LogInformation("Planned TeamLab runtime {RuntimeId} for game {GameId}, team {TeamId} on node {NodeId}.",
            runtime.Id, gameId, teamId, runtime.WorkerNodeId);
        logger.SystemLog($"TeamLab runtime planned: game={gameId}, team={teamId}, runtime={runtime.Id}, shards={shardPlan.Shards.Count}, udp={publicPort.Value}.",
            TaskStatus.Success, LogLevel.Information);
        return new TeamLabPlanResult(true, "TeamLab runtime planned.", runtime);
    }

    private static void AddEvent(TeamLabRuntime runtime, string stage, TeamLabEventLevel level, string message) =>
        runtime.Events.Add(new TeamLabEvent { Stage = stage, Level = level, Message = message });

    private async Task<TeamLabPublishedTopologyResult> LoadPublishedTopologyAsync(int gameId, int publishedVersion,
        CancellationToken token)
    {
        var snapshot = await context.PenetrationPublishedSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.GameId == gameId && s.PublishedVersion == publishedVersion, token);
        if (snapshot is null)
            return TeamLabPublishedTopologyResult.Failed("TeamLab published topology snapshot was not found.");

        var templateIds = ExtractTemplateIds(snapshot.SnapshotJson);
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, token);

        return TeamLabPublishedTopologyService.ParsePublishedSnapshot(gameId, publishedVersion,
            snapshot.SnapshotJson, templates);
    }

    private static HashSet<int> ExtractTemplateIds(string snapshotJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array)
                return [];

            return nodes.EnumerateArray()
                .Select(node => TryGetPropertyIgnoreCase(node, "imageTemplateId", out var templateId) &&
                                templateId.ValueKind == JsonValueKind.Number
                    ? templateId.GetInt32()
                    : 0)
                .Where(id => id > 0)
                .ToHashSet();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void ApplyShardPlan(TeamLabRuntime runtime, TeamLabShardPlanResult shardPlan,
        TeamLabPublishedAssetPlanResult assetPlan)
    {
        runtime.Shards.Clear();
        runtime.Networks.Clear();
        runtime.Assets.Clear();

        var networkByKey = assetPlan.Networks.ToDictionary(network => network.TopologyKey, StringComparer.Ordinal);
        var assetsByKey = assetPlan.Assets.ToDictionary(asset => asset.TopologyKey, StringComparer.Ordinal);

        foreach (var plan in shardPlan.Shards)
        {
            var shard = new TeamLabRuntimeShard
            {
                Runtime = runtime,
                RuntimeId = runtime.Id,
                WorkerNodeId = plan.WorkerNodeId,
                Status = TeamLabRuntimeStatus.Scheduled,
                RouteVersion = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            runtime.Shards.Add(shard);

            foreach (var networkKey in plan.NetworkKeys)
            {
                var spec = networkByKey[networkKey];
                runtime.Networks.Add(new TeamLabRuntimeNetwork
                {
                    Runtime = runtime,
                    RuntimeId = runtime.Id,
                    Shard = shard,
                    WorkerNodeId = plan.WorkerNodeId,
                    TopologyKey = spec.TopologyKey,
                    Name = spec.Name,
                    Cidr = spec.Cidr,
                    GatewayIp = spec.GatewayIp,
                    BridgeName = spec.BridgeName
                });
            }

            foreach (var assetKey in plan.AssetKeys)
            {
                var spec = assetsByKey[assetKey];
                var primary = spec.Interfaces.FirstOrDefault(i => i.IsPrimary) ?? spec.Interfaces.FirstOrDefault();
                runtime.Assets.Add(new TeamLabRuntimeAsset
                {
                    Runtime = runtime,
                    RuntimeId = runtime.Id,
                    Shard = shard,
                    WorkerNodeId = plan.WorkerNodeId,
                    Kind = spec.Kind == TeamLabAssetSpecKind.Docker ? TeamLabResourceKind.Docker : TeamLabResourceKind.Vm,
                    TopologyKey = spec.TopologyKey,
                    Name = spec.Name,
                    SourceTemplateId = spec.SourceTemplateId,
                    Image = spec.Image,
                    NetworkKey = primary?.NetworkKey,
                    IpAddress = primary?.IpAddress,
                    MacAddress = primary?.MacAddress,
                    InterfaceSummaryJson = JsonSerializer.Serialize(spec.Interfaces, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    Status = TeamLabRuntimeStatus.Scheduled
                });
            }
        }
    }

    private static string AllocateRuntimeCidr(string baseCidr, int prefixLength, int runtimeId)
    {
        var parts = baseCidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var baseAddress) ||
            !int.TryParse(parts[1], out var basePrefix) || basePrefix < 8 || prefixLength < basePrefix || prefixLength > 30)
            return string.Empty;

        var baseValue = ToUInt32(baseAddress);
        var subnetSize = 1u << (32 - prefixLength);
        var subnetCount = 1u << (prefixLength - basePrefix);
        var index = subnetCount == 0 ? 0u : (uint)Math.Max(0, runtimeId - 1) % subnetCount;
        return $"{FromUInt32(baseValue + index * subnetSize)}/{prefixLength}";
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return 0;
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new([
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ]);
}
