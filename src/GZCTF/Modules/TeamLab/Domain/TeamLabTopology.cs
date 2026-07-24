namespace GZCTF.Modules.TeamLab.Domain;

public sealed class TeamLabTopology
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public Guid? OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Revision { get; set; }
    public int SchemaVersion { get; set; } = 2;
    public Guid? CreatedByOperationId { get; set; }
    public Guid? LastMutationOperationId { get; set; }
    public string EditorMetadataJson { get; set; } = "{\"networks\":{},\"assets\":{}}";
    public string InfrastructureJson { get; set; } = "[]";
    public string DependenciesJson { get; set; } = "[]";
    public string ObservationJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<TeamLabTopologyNetwork> Networks { get; set; } = [];
    public List<TeamLabTopologyAsset> Assets { get; set; } = [];
    public List<TeamLabTopologyConnection> Connections { get; set; } = [];
    public List<TeamLabTopologyRelease> Releases { get; set; } = [];
}

public sealed class TeamLabTopologyNetwork
{
    public int Id { get; set; }
    public int TopologyId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AddressPoolCidr { get; set; } = string.Empty;
    public int RuntimePrefixLength { get; set; }
    public bool IsEntry { get; set; }
    public int OrderIndex { get; set; }
    public TeamLabTopology Topology { get; set; } = null!;
    public List<TeamLabTopologyInterface> Interfaces { get; set; } = [];
}

public sealed class TeamLabTopologyAsset
{
    public int Id { get; set; }
    public int TopologyId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TeamLabAssetKind Kind { get; set; }
    public int? ImageTemplateId { get; set; }
    public int CpuUnits { get; set; } = 10;
    public int MemoryMiB { get; set; } = 512;
    public int StorageMiB { get; set; } = 512;
    public int? ExposePort { get; set; }
    public bool RoutingEnabled { get; set; }
    public string EnvironmentJson { get; set; } = "{}";
    public string? StartCommand { get; set; }
    public TeamLabHealthCheckKind? HealthCheckKind { get; set; }
    public int? HealthCheckPort { get; set; }
    public int OrderIndex { get; set; }
    public bool Stateless { get; set; }
    public string? BootstrapJson { get; set; }
    public TeamLabEndpointObservationMode EndpointObservation { get; set; }
    public bool BakeAtPublish { get; set; }
    public TeamLabTopology Topology { get; set; } = null!;
    public List<TeamLabTopologyInterface> Interfaces { get; set; } = [];
}

public sealed class TeamLabTopologyInterface
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public int NetworkId { get; set; }
    public string Key { get; set; } = string.Empty;
    public int HostOffset { get; set; }
    public bool IsPrimary { get; set; }
    public int OrderIndex { get; set; }
    public TeamLabTopologyAsset Asset { get; set; } = null!;
    public TeamLabTopologyNetwork Network { get; set; } = null!;
}

public sealed class TeamLabTopologyConnection
{
    public int Id { get; set; }
    public int TopologyId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string FromNetworkKey { get; set; } = string.Empty;
    public string ToNetworkKey { get; set; } = string.Empty;
    public string? ViaAssetKey { get; set; }
    public string? ViaNodeKey { get; set; }
    public TeamLabConnectionDirection Direction { get; set; } = TeamLabConnectionDirection.Bidirectional;
    public TeamLabTopology Topology { get; set; } = null!;
}
