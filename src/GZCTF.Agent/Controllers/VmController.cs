using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/vms")]
public class VmController(KvmService kvm, AgentOperationGate gate) : ControllerBase
{
    private readonly KvmService _kvm = kvm;

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateVmRequest request, CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.VmCreate, token);
        var result = await _kvm.CreateVmAsync(request, token);
        return result is null
            ? throw new AgentOperationException("Kvm", "kvm.operation_failed", "VM creation failed.", true)
            : Ok(result);
    }

    [HttpDelete("{vmName}")]
    public async Task<IActionResult> Destroy(
        string vmName,
        [FromQuery] int? generation,
        [FromQuery] string? nativeId,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        await _kvm.DestroyVmAsync(vmName, generation, nativeId, token);
        return NoContent();
    }

    [HttpGet("{vmName}/ip")]
    public async Task<IActionResult> GetIp(string vmName, CancellationToken token)
    {
        var lookup = await _kvm.GetIpAddressWithDiagnosticAsync(vmName, token);
        var rdpPort = string.IsNullOrEmpty(lookup.IpAddress)
            ? null
            : await _kvm.EnsureRdpProxyAsync(vmName, lookup.IpAddress, token);

        return Ok(new VmIpResponse
        {
            VmName = vmName,
            IpAddress = lookup.IpAddress,
            RdpPort = rdpPort,
            Status = string.IsNullOrEmpty(lookup.IpAddress) ? "Pending" : "Ready",
            Diagnostic = lookup.Diagnostic
        });
    }

    [HttpPost("{vmName}/ip")]
    public async Task<IActionResult> GetIpWithInterfaces(string vmName, [FromBody] VmIpQueryRequest request,
        CancellationToken token)
    {
        var lookup = await _kvm.GetIpAddressWithDiagnosticAsync(vmName, token, request.Interfaces);
        var rdpPort = string.IsNullOrEmpty(lookup.IpAddress)
            ? null
            : await _kvm.EnsureRdpProxyAsync(vmName, lookup.IpAddress, token);

        return Ok(new VmIpResponse
        {
            VmName = vmName,
            IpAddress = lookup.IpAddress,
            RdpPort = rdpPort,
            Status = string.IsNullOrEmpty(lookup.IpAddress) ? "Pending" : "Ready",
            Diagnostic = lookup.Diagnostic
        });
    }
}
