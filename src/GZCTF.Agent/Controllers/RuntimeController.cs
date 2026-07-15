using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/runtime")]
public sealed class RuntimeController(
    DockerService docker,
    KvmService kvm,
    AgentCapabilityService capabilities) : ControllerBase
{
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(CancellationToken token)
    {
        var manifest = await capabilities.GetManifestAsync(
            await capabilities.GetBinarySha256Async(), token);
        var dockerSupported = manifest.Features.Contains(AgentFeatureIds.Docker, StringComparer.Ordinal);
        var kvmSupported = manifest.Features.Contains(AgentFeatureIds.Kvm, StringComparer.Ordinal);

        var containers = dockerSupported
            ? await docker.GetManagedRuntimeInventoryAsync(token)
            : [];
        var vms = kvmSupported
            ? await kvm.GetManagedRuntimeInventoryAsync(token)
            : [];

        return Ok(new RuntimeInventoryResponse(
            dockerSupported,
            kvmSupported,
            containers,
            vms,
            DateTimeOffset.UtcNow));
    }
}
