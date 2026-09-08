using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/runtime")]
public sealed class RuntimeController(
    DockerService docker,
    KvmService kvm,
    TeamLabNetworkService teamLab,
    AgentCapabilityService capabilities,
    ILogger<RuntimeController> logger) : ControllerBase
{
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(CancellationToken token)
    {
        var manifest = await capabilities.GetManifestAsync(
            await capabilities.GetBinarySha256Async(), token);
        var dockerSupported = manifest.Features.Contains(AgentFeatureIds.Docker, StringComparer.Ordinal);
        var kvmSupported = manifest.Features.Contains(AgentFeatureIds.Kvm, StringComparer.Ordinal);

        var containers = await ReadComputeAsync(dockerSupported, docker.GetManagedRuntimeInventoryAsync, "Docker", logger, token);
        var vms = await ReadComputeAsync(kvmSupported, kvm.GetManagedRuntimeInventoryAsync, "KVM", logger, token);
        var teamLabResources = await teamLab.GetManagedRuntimeInventoryAsync(token);

        return Ok(new RuntimeInventoryResponse(
            containers.Available,
            vms.Available,
            containers.Resources,
            vms.Resources,
            DateTimeOffset.UtcNow,
            teamLabResources));
    }

    internal static async Task<(bool Available, IReadOnlyList<RuntimeInventoryResource> Resources)> ReadComputeAsync(
        bool supported, Func<CancellationToken, Task<IReadOnlyList<RuntimeInventoryResource>>> read,
        string kind, ILogger logger, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!supported) return (false, []);
        try { return (true, await read(token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "{Kind} inventory is unavailable; resources must not be treated as absent.", kind);
            return (false, []);
        }
    }
}
