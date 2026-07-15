namespace GZCTF.Agent.Models;

public sealed record RuntimeInventoryResource(
    string NativeId,
    string StableName,
    int Generation,
    string State,
    string? Image = null);

public sealed record RuntimeInventoryResponse(
    bool DockerSupported,
    bool KvmSupported,
    IReadOnlyList<RuntimeInventoryResource> Containers,
    IReadOnlyList<RuntimeInventoryResource> Vms,
    DateTimeOffset ObservedAt);
