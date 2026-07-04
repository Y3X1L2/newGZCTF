using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabPublishedTopologyResult(bool Success, string Message, PenetrationConfig? Config)
{
    public static TeamLabPublishedTopologyResult Failed(string message) => new(false, message, null);
}

public static class TeamLabPublishedTopologyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static TeamLabPublishedTopologyResult ParsePublishedSnapshot(int gameId, int publishedVersion,
        string snapshotJson, IReadOnlyDictionary<int, ImageTemplate> templates)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return TeamLabPublishedTopologyResult.Failed("Published TeamLab topology snapshot is empty.");

        PenetrationConfigModel? model;
        try
        {
            model = JsonSerializer.Deserialize<PenetrationConfigModel>(snapshotJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return TeamLabPublishedTopologyResult.Failed($"Published TeamLab topology snapshot is invalid: {ex.Message}");
        }

        if (model is null)
            return TeamLabPublishedTopologyResult.Failed("Published TeamLab topology snapshot is invalid.");

        model.GameId = gameId;
        model.PublishedVersion = publishedVersion;
        model.Status = PenetrationDeploymentStatus.Published;

        var config = new PenetrationConfig
        {
            Id = -gameId,
            GameId = gameId,
            BaseCidr = string.IsNullOrWhiteSpace(model.BaseCidr) ? "10.60.0.0/16" : model.BaseCidr.Trim(),
            TeamSubnetPrefix = model.TeamSubnetPrefix is >= 16 and <= 28 ? model.TeamSubnetPrefix : 24,
            NetworkSubnetPrefix = model.NetworkSubnetPrefix is >= 24 and <= 30 ? model.NetworkSubnetPrefix : 28,
            MaxResetCount = Math.Clamp(model.MaxResetCount, 0, 100),
            PublishedVersion = publishedVersion,
            Status = PenetrationDeploymentStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow
        };

        if (model.Networks.Count == 0)
            return TeamLabPublishedTopologyResult.Failed("Published TeamLab topology has no LabNetwork.");

        var networks = new Dictionary<int, PenetrationNetwork>();
        foreach (var networkModel in model.Networks.OrderBy(n => n.OrderIndex))
        {
            var network = new PenetrationNetwork
            {
                Id = networkModel.Id,
                Config = config,
                ConfigId = config.Id,
                TopologyKey = NormalizeKey(networkModel.TopologyKey, "network", networkModel.Id),
                Name = string.IsNullOrWhiteSpace(networkModel.Name) ? "Unnamed Network" : networkModel.Name.Trim(),
                Slug = string.IsNullOrWhiteSpace(networkModel.Slug) ? NormalizeKey(networkModel.Name, "network", networkModel.Id) : networkModel.Slug.Trim(),
                Cidr = string.IsNullOrWhiteSpace(networkModel.Cidr) ? null : networkModel.Cidr.Trim(),
                ZoneType = NormalizeZoneType(networkModel.ZoneType),
                TrustLevel = Math.Clamp(networkModel.TrustLevel, 0, 100),
                Description = string.IsNullOrWhiteSpace(networkModel.Description) ? null : networkModel.Description.Trim(),
                DefaultPolicy = networkModel.DefaultPolicy,
                IsEntry = false,
                OrderIndex = networkModel.OrderIndex,
                PositionX = networkModel.PositionX,
                PositionY = networkModel.PositionY,
                Width = networkModel.Width,
                Height = networkModel.Height,
                Collapsed = networkModel.Collapsed
            };
            config.Networks.Add(network);
            networks[networkModel.Id] = network;
        }

        if (model.Nodes.Count == 0)
            return TeamLabPublishedTopologyResult.Failed("Published TeamLab topology has no asset node.");

        var nodes = new Dictionary<int, PenetrationNode>();
        foreach (var nodeModel in model.Nodes.OrderBy(n => n.OrderIndex))
        {
            var networkId = networks.ContainsKey(nodeModel.NetworkId)
                ? nodeModel.NetworkId
                : networks.Keys.OrderBy(id => id).First();
            var network = networks[networkId];

            if (nodeModel.ImageTemplateId is not { } templateId ||
                !templates.TryGetValue(templateId, out var template) ||
                template.Status != ImageStatus.Ready)
                return TeamLabPublishedTopologyResult.Failed(
                    $"Node {nodeModel.Name} must bind a ready image template before TeamLab deployment.");

            var node = new PenetrationNode
            {
                Id = nodeModel.Id,
                Config = config,
                ConfigId = config.Id,
                Network = network,
                NetworkId = network.Id,
                TopologyKey = NormalizeKey(nodeModel.TopologyKey, "node", nodeModel.Id),
                Name = string.IsNullOrWhiteSpace(nodeModel.Name) ? "Unnamed Node" : nodeModel.Name.Trim(),
                Description = TrimToNull(nodeModel.Description),
                PlayerAlias = TrimToNull(nodeModel.PlayerAlias),
                PlayerDescription = TrimToNull(nodeModel.PlayerDescription),
                NodeType = NormalizeNodeType(nodeModel.NodeType),
                ImageTemplateId = templateId,
                ImageTemplate = template,
                ImageName = TrimToNull(nodeModel.ImageName),
                CpuCount = Math.Clamp(nodeModel.CpuCount, 1, 128),
                MemoryLimit = Math.Clamp(nodeModel.MemoryLimit, 64, 262144),
                StorageLimit = Math.Clamp(nodeModel.StorageLimit, 64, 1048576),
                ExposePort = Math.Clamp(nodeModel.ExposePort, 1, 65535),
                IsEntry = false,
                PublishPort = false,
                AllowRouting = nodeModel.AllowRouting,
                StaticIp = TrimToNull(nodeModel.StaticIp),
                EnvironmentVariables = JsonSerializer.Serialize(nodeModel.EnvironmentVariables ?? [], JsonOptions),
                StartCommand = TrimToNull(nodeModel.StartCommand),
                HealthCheck = TrimToNull(nodeModel.HealthCheck),
                ReservedAdRole = TrimToNull(nodeModel.ReservedAdRole),
                PositionX = nodeModel.PositionX,
                PositionY = nodeModel.PositionY,
                OrderIndex = nodeModel.OrderIndex
            };

            config.Nodes.Add(node);
            network.Nodes.Add(node);
            nodes[nodeModel.Id] = node;

            foreach (var itemModel in nodeModel.ScoreItems.OrderBy(i => i.OrderIndex))
            {
                node.ScoreItems.Add(new PenetrationScoreItem
                {
                    Id = itemModel.Id,
                    Node = node,
                    NodeId = node.Id,
                    TopologyKey = NormalizeKey(itemModel.TopologyKey, "score", itemModel.Id),
                    Title = string.IsNullOrWhiteSpace(itemModel.Title) ? "Unnamed Score Item" : itemModel.Title.Trim(),
                    Description = TrimToNull(itemModel.Description),
                    Category = string.IsNullOrWhiteSpace(itemModel.Category) ? "General" : itemModel.Category.Trim(),
                    Score = Math.Max(0, itemModel.Score),
                    IsDynamic = itemModel.IsDynamic,
                    StaticFlag = TrimToNull(itemModel.StaticFlag),
                    FlagTemplate = TrimToNull(itemModel.FlagTemplate),
                    MaxAttempts = Math.Max(0, itemModel.MaxAttempts),
                    IsVisible = itemModel.IsVisible,
                    IsCheckpoint = itemModel.IsCheckpoint,
                    PrerequisiteItemIds = JsonSerializer.Serialize(itemModel.PrerequisiteItemIds ?? [], JsonOptions),
                    OrderIndex = itemModel.OrderIndex
                });
            }
        }

        var interfaceModels = model.Interfaces.Count > 0
            ? model.Interfaces
            : model.Nodes.SelectMany(n => n.Interfaces).ToList();

        foreach (var interfaceModel in interfaceModels.OrderBy(i => i.OrderIndex))
        {
            if (!nodes.TryGetValue(interfaceModel.NodeId, out var node))
                continue;

            var network = networks.GetValueOrDefault(interfaceModel.NetworkId) ?? node.Network;
            var iface = new PenetrationInterface
            {
                Id = interfaceModel.Id,
                Node = node,
                NodeId = node.Id,
                Network = network,
                NetworkId = network.Id,
                TopologyKey = NormalizeKey(interfaceModel.TopologyKey, "iface", interfaceModel.Id),
                Name = string.IsNullOrWhiteSpace(interfaceModel.Name) ? "eth0" : interfaceModel.Name.Trim(),
                StaticIp = TrimToNull(interfaceModel.StaticIp),
                IsPrimary = interfaceModel.IsPrimary,
                IsManagement = interfaceModel.IsManagement,
                OrderIndex = interfaceModel.OrderIndex
            };
            node.Interfaces.Add(iface);
            network.Interfaces.Add(iface);
        }

        foreach (var node in config.Nodes.Where(n => n.Interfaces.Count > 0 && n.Interfaces.All(i => !i.IsPrimary)))
            node.Interfaces.OrderBy(i => i.OrderIndex).First().IsPrimary = true;

        foreach (var edgeModel in model.Edges.OrderBy(e => e.Priority).ThenBy(e => e.Id))
        {
            var edge = new PenetrationEdge
            {
                Id = edgeModel.Id,
                Config = config,
                ConfigId = config.Id,
                TopologyKey = NormalizeKey(edgeModel.TopologyKey, "edge", edgeModel.Id),
                SourceNodeId = edgeModel.SourceNodeId,
                TargetNodeId = edgeModel.TargetNodeId,
                SourceKind = edgeModel.SourceKind,
                SourceId = edgeModel.SourceId,
                TargetKind = edgeModel.TargetKind,
                TargetId = edgeModel.TargetId,
                Protocol = edgeModel.Protocol,
                PortRange = string.IsNullOrWhiteSpace(edgeModel.PortRange) ? "any" : edgeModel.PortRange.Trim(),
                PolicyAction = edgeModel.PolicyAction,
                IsRouteHint = edgeModel.IsRouteHint,
                EnforcementMode = edgeModel.EnforcementMode,
                Priority = edgeModel.Priority,
                Label = TrimToNull(edgeModel.Label),
                Description = TrimToNull(edgeModel.Description)
            };
            config.Edges.Add(edge);
        }

        return new TeamLabPublishedTopologyResult(true, "Published TeamLab topology parsed.", config);
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PenetrationZoneType NormalizeZoneType(PenetrationZoneType value) =>
        value == PenetrationZoneType.Public ? PenetrationZoneType.Dmz : value;

    private static PenetrationNodeType NormalizeNodeType(PenetrationNodeType value) =>
        value == PenetrationNodeType.Entry ? PenetrationNodeType.Web : value;

    private static string NormalizeKey(string? value, string prefix, int id)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
            return trimmed.Length <= 64 ? trimmed : trimmed[..64];

        return id > 0 ? $"{prefix}-{id}" : $"{prefix}-{Guid.NewGuid():N}";
    }
}
