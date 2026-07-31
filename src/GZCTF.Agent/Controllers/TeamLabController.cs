using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Observation;
using GZCTF.Agent.Services.RuntimeSignals;
using GZCTF.Agent.Services.TeamLab;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/teamlab")]
public class TeamLabController(
    TeamLabNetworkService service,
    TeamLabPacketObserver observer,
    EndpointSensorChannelService sensors,
    TeamLabPcapService pcap,
    TeamLabContainerNetworkFinalizeService containerNetworkFinalize,
    AgentRuntimeSignalJournal runtimeSignals,
    AgentOperationGate gate) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken token) => Ok(await service.GetStatusAsync(token));

    [HttpPost("shards/apply")]
    public async Task<IActionResult> ApplyInfrastructure(
        [FromBody] TeamLabInfrastructureApplyRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        return Ok(await service.ApplyInfrastructureAsync(request, token));
    }

    [HttpGet("runtime/{runtimeId:int}/generation/{generation:int}/state")]
    public async Task<IActionResult> InfrastructureState(
        int runtimeId,
        int generation,
        CancellationToken token) =>
        Ok(await service.GetInfrastructureStateAsync(runtimeId, generation, token));

    [HttpPost("wireguard")]
    public async Task<IActionResult> ConfigureWireGuard([FromBody] TeamLabWireGuardRequest request, CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        return Ok(await service.ConfigureWireGuardAsync(request, token));
    }

    [HttpPost("wireguard/cleanup")]
    public async Task<IActionResult> CleanupWireGuard(
        [FromBody] TeamLabWireGuardCleanupRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await service.CleanupWireGuardAsync(request, token));
    }

    [HttpPost("cleanup")]
    public async Task<IActionResult> Cleanup([FromBody] TeamLabCleanupRequest request, CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        var result = await service.CleanupAsync(request, token);
        if (result.Success && !result.DryRun)
            await runtimeSignals.DeleteAcknowledgedGenerationAsync(
                request.RuntimeId, request.Generation, token);
        return Ok(result);
    }

    [HttpPost("probe")]
    public async Task<IActionResult> Probe([FromBody] TeamLabProbeRequest request, CancellationToken token) =>
        Ok(await service.ProbeAsync(request, token));

    [HttpPost("containers/attach")]
    public async Task<IActionResult> AttachContainer([FromBody] TeamLabContainerAttachRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        return Ok(await service.AttachContainerAsync(request, token));
    }

    [HttpPost("containers/network/finalize")]
    public async Task<IActionResult> FinalizeContainerNetwork(
        [FromBody] TeamLabContainerNetworkFinalizeRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        return Ok(await containerNetworkFinalize.FinalizeAsync(request, token));
    }

    [HttpPost("capture/start")]
    public async Task<IActionResult> StartCapture([FromBody] TeamLabCaptureStartRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        return Ok(await pcap.StartAsync(request, token));
    }

    [HttpPost("capture/stop")]
    public async Task<IActionResult> StopCapture([FromBody] TeamLabCaptureStopRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await pcap.StopAsync(request, token));
    }

    [HttpPost("capture/status")]
    public async Task<IActionResult> CaptureStatus([FromBody] TeamLabCaptureStatusRequest request,
        CancellationToken token) =>
        Ok(await pcap.StatusAsync(request, token));

    [HttpPost("capture/upload")]
    public async Task<IActionResult> UploadCapture(
        [FromBody] TeamLabCaptureUploadRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await pcap.UploadAsync(request, token));
    }

    [HttpPost("capture/delete")]
    public async Task<IActionResult> DeleteCapture(
        [FromBody] TeamLabCaptureDeleteRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await pcap.DeleteAsync(request, token));
    }

    [HttpPost("observations/read")]
    public IActionResult ReadObservations([FromBody] TeamLabObservationBatchRequest request)
    {
        if (request.RuntimeId <= 0 || request.Generation <= 0 || request.AfterSequence < 0)
            return BadRequest("Invalid TeamLab observation cursor.");
        return Ok(observer.Read(request));
    }

    [HttpPost("sensors/register")]
    public IActionResult RegisterSensor([FromBody] TeamLabEndpointSensorRegistrationRequest request) =>
        Ok(sensors.Register(request));

    [HttpPost("sensors/remove")]
    public IActionResult RemoveSensor([FromBody] TeamLabEndpointSensorRemoveRequest request) =>
        Ok(sensors.Remove(request.RuntimeId, request.Generation, request.AssetKey));

    [HttpPost("sensors/start")]
    public async Task<IActionResult> StartSensor(
        [FromBody] TeamLabEndpointSensorStartRequest request,
        CancellationToken token) =>
        Ok(await sensors.StartAsync(request, token));
}
