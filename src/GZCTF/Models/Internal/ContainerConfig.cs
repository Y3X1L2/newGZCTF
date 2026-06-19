namespace GZCTF.Models.Internal;

public class ContainerConfig
{
    /// <summary>
    /// Container image
    /// </summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// Team ID
    /// </summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>
    /// Challenge ID
    /// </summary>
    public int ChallengeId { get; set; }

    /// <summary>
    /// User ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Port to be exposed by the container
    /// </summary>
    public int ExposedPort { get; set; }

    /// <summary>
    /// Flag text
    /// </summary>
    public string? Flag { get; set; } = string.Empty;

    /// <summary>
    /// Whether to record traffic
    /// </summary>
    public bool EnableTrafficCapture { get; set; }

    /// <summary>
    /// Memory limit (MB)
    /// </summary>
    public int MemoryLimit { get; set; } = 64;

    /// <summary>
    /// CPU limit (0.1 CPUs)
    /// </summary>
    public int CPUCount { get; set; } = 1;

    /// <summary>
    /// Storage write limit
    /// </summary>
    public int StorageLimit { get; set; } = 256;

    /// <summary>
    /// Container network mode
    /// </summary>
    public NetworkMode NetworkMode { get; set; } = NetworkMode.Open;

    /// <summary>
    /// Custom Docker network name (overrides NetworkMode when set)
    /// </summary>
    public string? NetworkName { get; set; }

    /// <summary>
    /// Fixed IPv4 address inside the primary custom network.
    /// </summary>
    public string? IPAddress { get; set; }

    /// <summary>
    /// Additional custom Docker network names to connect after creation.
    /// </summary>
    public List<string> AdditionalNetworkNames { get; set; } = [];

    /// <summary>
    /// Optional CIDR subnets for custom Docker networks, keyed by network name.
    /// </summary>
    public Dictionary<string, string> NetworkSubnets { get; set; } = [];

    /// <summary>
    /// Whether the exposed port should be published to the host.
    /// </summary>
    public bool PublishPort { get; set; } = true;

    /// <summary>
    /// Extra container environment variables.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];

    /// <summary>
    /// Optional command override.
    /// </summary>
    public string? StartCommand { get; set; }

    /// <summary>
    /// Optional shell health check command.
    /// </summary>
    public string? HealthCheck { get; set; }

    /// <summary>
    /// Use host/agent managed penetration fabric instead of Docker network attachments.
    /// </summary>
    public bool UsePenetrationFabric { get; set; }

    /// <summary>
    /// Grant NET_ADMIN so platform-generated runtime routes can be installed.
    /// </summary>
    public bool EnableNetworkAdmin { get; set; }

    /// <summary>
    /// Remove the default route after startup so runtime reachability is controlled by explicit routes.
    /// </summary>
    public bool RemoveDefaultRoute { get; set; }

    /// <summary>
    /// Enable IPv4 forwarding for route nodes.
    /// </summary>
    public bool EnableIpForwarding { get; set; }

    /// <summary>
    /// Optional fixed fleet node target. When set, fleet deployment must use this node.
    /// </summary>
    public Guid? PreferredNodeId { get; set; }

    /// <summary>
    /// Internal scheduler hint: capacity has already been reserved by a higher-level batch deployment.
    /// </summary>
    public bool FleetCapacityReserved { get; set; }

    /// <summary>
    /// Explicit network attachments. When present, these supersede NetworkName/IPAddress/AdditionalNetworkNames.
    /// </summary>
    public List<ContainerNetworkAttachment> NetworkAttachments { get; set; } = [];
}

public class ContainerNetworkAttachment
{
    public string NetworkName { get; set; } = string.Empty;
    public string? SubnetCidr { get; set; }
    public string? IPAddress { get; set; }
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Create the Docker bridge as an internal network, preventing direct external routing for inner segments.
    /// </summary>
    public bool IsInternal { get; set; }
}
