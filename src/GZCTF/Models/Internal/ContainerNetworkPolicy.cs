namespace GZCTF.Models.Internal;

public class ContainerNetworkPolicySet
{
    public string SetName { get; set; } = string.Empty;

    public List<ContainerNetworkPolicyRule> Rules { get; set; } = [];
}

public class ContainerNetworkPolicyRule
{
    public string Source { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string Protocol { get; set; } = "any";

    public string PortRange { get; set; } = "any";

    public bool Allow { get; set; }

    public string Comment { get; set; } = string.Empty;
}

public sealed record ContainerNetworkPolicyResult(bool Succeeded, bool IsSupported, string Message)
{
    public static ContainerNetworkPolicyResult Success(string? message = null) =>
        new(true, true, string.IsNullOrWhiteSpace(message) ? "Network policy applied" : message);

    public static ContainerNetworkPolicyResult Failed(string message) =>
        new(false, true, string.IsNullOrWhiteSpace(message) ? "Network policy operation failed" : message);

    public static ContainerNetworkPolicyResult Unsupported(string message) =>
        new(false, false, string.IsNullOrWhiteSpace(message) ? "Network policy is not supported" : message);
}
