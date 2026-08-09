using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.RuntimeSignals;
using GZCTF.Agent.Services.Vm;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/vms")]
public class VmController(
    KvmService kvm,
    VmGuestAgentService guest,
    VmBootstrapService bootstrap,
    VmRuntimeReadinessCoordinator readiness,
    AgentRuntimeSignalPublisher signals,
    ILogger<VmController> logger,
    AgentOperationGate gate) : ControllerBase
{
    private readonly KvmService _kvm = kvm;

    [HttpPost("{vmName}/wait-clean-shutdown")]
    public async Task<IActionResult> WaitCleanShutdown(
        string vmName,
        [FromQuery] int timeoutSeconds,
        CancellationToken token) =>
        Ok(new { cleanShutdown = await _kvm.WaitForCleanShutdownAsync(vmName, timeoutSeconds, token) });

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateVmRequest request, CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.VmCreate, token);
        var result = await _kvm.CreateVmAsync(request, token);
        if (result is not null && request.GuestSupervisor is null)
            await readiness.TrackAsync(request, result, token);
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
    public async Task<IActionResult> GetIp(
        string vmName,
        [FromQuery] int? generation,
        [FromQuery] string? nativeId,
        [FromQuery] int? rdpPort,
        CancellationToken token)
    {
        var targetPort = rdpPort ?? 3389;
        var response = await _kvm.ExecuteWithIdentityAsync(
            vmName, generation, nativeId, async identityToken =>
            {
                var lookup = await _kvm.GetIpAddressWithDiagnosticAsync(vmName, identityToken);
                var rdpReady = !string.IsNullOrEmpty(lookup.IpAddress) &&
                               await KvmService.IsTcpPortReadyAsync(lookup.IpAddress, targetPort, identityToken);
                var proxyPort = !rdpReady
                    ? null
                    : await _kvm.EnsureRdpProxyAsync(vmName, lookup.IpAddress!, targetPort, identityToken);
                return new VmIpResponse
                {
                    VmName = vmName,
                    IpAddress = lookup.IpAddress,
                    RdpPort = proxyPort,
                    Status = proxyPort.HasValue ? "Ready" : "Pending",
                    Diagnostic = rdpReady
                        ? lookup.Diagnostic
                        : $"{lookup.Diagnostic} RDP target port {targetPort} is not ready.".Trim()
                };
            }, token);
        return Ok(response);
    }

    [HttpPost("{vmName}/ip")]
    public async Task<IActionResult> GetIpWithInterfaces(
        string vmName,
        [FromQuery] int? generation,
        [FromQuery] string? nativeId,
        [FromQuery] int? rdpPort,
        [FromBody] VmIpQueryRequest request,
        CancellationToken token)
    {
        var targetPort = rdpPort ?? 3389;
        var response = await _kvm.ExecuteWithIdentityAsync(
            vmName, generation, nativeId, async identityToken =>
            {
                var lookup = await _kvm.GetIpAddressWithDiagnosticAsync(
                    vmName, identityToken, request.Interfaces);
                var rdpReady = !string.IsNullOrEmpty(lookup.IpAddress) &&
                               await KvmService.IsTcpPortReadyAsync(lookup.IpAddress, targetPort, identityToken);
                var proxyPort = !rdpReady
                    ? null
                    : await _kvm.EnsureRdpProxyAsync(vmName, lookup.IpAddress!, targetPort, identityToken);
                return new VmIpResponse
                {
                    VmName = vmName,
                    IpAddress = lookup.IpAddress,
                    RdpPort = proxyPort,
                    Status = proxyPort.HasValue ? "Ready" : "Pending",
                    Diagnostic = rdpReady
                        ? lookup.Diagnostic
                        : $"{lookup.Diagnostic} RDP target port {targetPort} is not ready.".Trim()
                };
            }, token);
        return Ok(response);
    }

    [HttpPost("{vmName}/guest/wait")]
    public async Task<IActionResult> WaitGuest(
        string vmName,
        [FromQuery] int? generation,
        [FromQuery] string? nativeId,
        [FromBody] VmGuestReadyRequest request,
        CancellationToken token)
    {
        var result = await _kvm.ExecuteWithIdentityAsync(
            vmName, generation, nativeId,
            identityToken => guest.WaitReadyAsync(
                vmName, TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 10, 600)), identityToken),
            token);
        return Ok(result);
    }

    [HttpPost("{vmName}/guest/execute")]
    public async Task<IActionResult> ExecuteGuest(
        string vmName,
        [FromQuery] int? generation,
        [FromQuery] string? nativeId,
        [FromBody] VmGuestCommandRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await _kvm.ExecuteWithIdentityAsync(
            vmName, generation, nativeId,
            identityToken => guest.ExecuteAsync(vmName, request, identityToken), token));
    }

    [HttpPost("{vmName}/bootstrap/apply")]
    public async Task<IActionResult> ApplyBootstrap(
        string vmName,
        [FromQuery] string? nativeId,
        [FromBody] VmBootstrapApplyRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        await EmitAsync(vmName, request, AgentRuntimeSignalStage.BootstrapRunning,
            AgentRuntimeSignalOutcome.Started, null, token);
        try
        {
            var result = await _kvm.ExecuteWithIdentityAsync(
                vmName, request.Generation, nativeId,
                identityToken => bootstrap.ApplyAsync(vmName, request, identityToken), token);
            await EmitAsync(
                vmName,
                request,
                result.Success ? AgentRuntimeSignalStage.BootstrapCompleted : AgentRuntimeSignalStage.Failed,
                result.Success ? AgentRuntimeSignalOutcome.Ready : AgentRuntimeSignalOutcome.Failed,
                result.Success ? null : "runtime.bootstrap_failed",
                token);
            return Ok(result);
        }
        catch (Exception exception)
        {
            await TryEmitFailureAsync(vmName, request, "runtime.bootstrap_failed", exception);
            throw;
        }
    }

    [HttpPost("{vmName}/bootstrap/health")]
    public async Task<IActionResult> CheckBootstrapHealth(
        string vmName,
        [FromQuery] string? nativeId,
        [FromBody] VmBootstrapApplyRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        try
        {
            var result = await _kvm.ExecuteWithIdentityAsync(
                vmName, request.Generation, nativeId,
                identityToken => bootstrap.CheckHealthAsync(vmName, request, identityToken), token);
            await EmitAsync(
                vmName,
                request,
                result.Success ? AgentRuntimeSignalStage.HealthReady : AgentRuntimeSignalStage.Failed,
                result.Success ? AgentRuntimeSignalOutcome.Ready : AgentRuntimeSignalOutcome.Failed,
                result.Success ? null : "runtime.health_failed",
                token);
            return Ok(result);
        }
        catch (Exception exception)
        {
            await TryEmitFailureAsync(vmName, request, "runtime.health_failed", exception);
            throw;
        }
    }

    [HttpPost("{vmName}/capabilities/probe")]
    public async Task<IActionResult> ProbeCapabilities(
        string vmName,
        [FromQuery] int? generation,
        [FromQuery] string? nativeId,
        [FromBody] VmCapabilityProbeRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await _kvm.ExecuteWithIdentityAsync(
            vmName, generation, nativeId,
            identityToken => bootstrap.ProbeAsync(vmName, request, identityToken), token));
    }

    private async Task EmitAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        AgentRuntimeSignalStage stage,
        AgentRuntimeSignalOutcome outcome,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        if (request.OperationId is not { } operationId || operationId == Guid.Empty)
            return;
        await signals.AppendAsync(new AgentRuntimeSignalDraft(
            operationId,
            request.RuntimeId,
            request.Generation,
            "vm",
            vmName,
            stage,
            outcome,
            errorCode,
            Retryable: false), cancellationToken);
    }

    private async Task TryEmitFailureAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        string errorCode,
        Exception exception)
    {
        try
        {
            await EmitAsync(vmName, request, AgentRuntimeSignalStage.Failed,
                AgentRuntimeSignalOutcome.Failed, errorCode, CancellationToken.None);
        }
        catch (Exception signalException)
        {
            logger.LogError(signalException,
                "Failed to persist VM operation failure signal after {ErrorType}", exception.GetType().Name);
        }
    }
}
