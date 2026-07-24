namespace GZCTF.Modules.Runtime.Contracts;

public sealed record AgentRuntimeInventoryResource(
    string NativeId,
    string StableName,
    int Generation,
    string State,
    string? Image = null,
    string ResourceKind = "workload",
    int? RuntimeId = null,
    string? DesiredStateDigest = null);

public sealed record AgentRuntimeInventoryResponse(
    bool DockerSupported,
    bool KvmSupported,
    IReadOnlyList<AgentRuntimeInventoryResource> Containers,
    IReadOnlyList<AgentRuntimeInventoryResource> Vms,
    DateTimeOffset ObservedAt,
    IReadOnlyList<AgentRuntimeInventoryResource>? TeamLabResources = null);
