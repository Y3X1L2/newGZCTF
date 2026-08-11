namespace GZCTF.Agent.Models;

public sealed record RuntimeInventoryResource(
    string NativeId,
    string StableName,
    int Generation,
    string State,
    string? Image = null,
    string ResourceKind = "workload",
    int? RuntimeId = null,
    string? DesiredStateDigest = null,
    string? AssetKey = null);

public sealed record RuntimeInventoryResponse(
    bool DockerSupported,
    bool KvmSupported,
    IReadOnlyList<RuntimeInventoryResource> Containers,
    IReadOnlyList<RuntimeInventoryResource> Vms,
    DateTimeOffset ObservedAt,
    IReadOnlyList<RuntimeInventoryResource>? TeamLabResources = null);
