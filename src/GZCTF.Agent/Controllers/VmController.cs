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
        CancellationToken token)
    {
        var response = await _kvm.ExecuteWithIdentityAsync(
            vmName, generation, nativeId, async identityToken =>
            {
                var lookup = await _kvm.GetIpAddressWithDiagnosticAsync(vmName, identityToken);
                var rdpPort = string.IsNullOrEmpty(lookup.IpAddress)
                    ? null
                    : await _kvm.EnsureRdpProxyAsync(vmName, lookup.IpAddress, identityToken);
                return new VmIpResponse
                {
                    VmName = vmName,
                    IpAddress = lookup.IpAddress,
                    RdpPort = rdpPort,
                    Status = string.IsNullOrEmpty(lookup.IpAddress) ? "Pending" : "Ready",
                    Diagnostic = lookup.Diagnostic
                };
            }, token);
        return Ok(response);
    }

    [HttpPost("{vmName}/ip")]
    public async Task<IActionResult> GetIpWithInterfaces(
        string vmName,
        [FromQuery] int? generation,
        [FromQuery] string? nativeId,
        [FromBody] VmIpQueryRequest request,
        CancellationToken token)
    {
        var response = await _kvm.ExecuteWithIdentityAsync(
            vmName, generation, nativeId, async identityToken =>
            {
                var lookup = await _kvm.GetIpAddressWithDiagnosticAsync(
                    vmName, identityToken, request.Interfaces);
                var rdpPort = string.IsNullOrEmpty(lookup.IpAddress)
                    ? null
                    : await _kvm.EnsureRdpProxyAsync(vmName, lookup.IpAddress, identityToken);
                return new VmIpResponse
                {
                    VmName = vmName,
                    IpAddress = lookup.IpAddress,
                    RdpPort = rdpPort,
                    Status = string.IsNullOrEmpty(lookup.IpAddress) ? "Pending" : "Ready",
                    Diagnostic = lookup.Diagnostic
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
            AgentRuntimeSignalOutcome.Started, null, null, token);
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
                result.Success ? null : BootstrapFailureFacts(result),
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
                result.Success ? null : BootstrapFailureFacts(result),
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
        IReadOnlyDictionary<string, string>? facts,
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
            Retryable: false,
            Facts: facts), cancellationToken);
    }

    private static IReadOnlyDictionary<string, string>? BootstrapFailureFacts(
        VmBootstrapApplyResponse result)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        Add("stage", result.Stage);
        Add("errorCode", result.ErrorCode);
        Add("stepId", result.FailedStep);
        Add("category", result.FailureCategory);
        if (result.ExitCode is { } exitCode)
            facts["exitCode"] = exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return facts.Count == 0 ? null : facts;

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                facts[key] = value.Length <= 256 ? value : value[..256];
        }
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
                AgentRuntimeSignalOutcome.Failed, errorCode, null, CancellationToken.None);
        }
        catch (Exception signalException)
        {
            logger.LogError(signalException,
                "Failed to persist VM operation failure signal after {ErrorType}", exception.GetType().Name);
        }
    }
}
