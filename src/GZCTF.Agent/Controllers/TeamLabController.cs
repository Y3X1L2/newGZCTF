using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/teamlab")]
public class TeamLabController(TeamLabNetworkService service) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken token) => Ok(await service.GetStatusAsync(token));

    [HttpPost("bridges")]
    public async Task<IActionResult> CreateBridge([FromBody] TeamLabBridgeRequest request, CancellationToken token) =>
        Ok(await service.CreateBridgeAsync(request, token));

    [HttpPost("routers")]
    public async Task<IActionResult> CreateRouter([FromBody] TeamLabRouterRequest request, CancellationToken token) =>
        Ok(await service.CreateRouterAsync(request, token));

    [HttpPost("wireguard")]
    public async Task<IActionResult> ConfigureWireGuard([FromBody] TeamLabWireGuardRequest request, CancellationToken token) =>
        Ok(await service.ConfigureWireGuardAsync(request, token));

    [HttpPost("cleanup")]
    public async Task<IActionResult> Cleanup([FromBody] TeamLabCleanupRequest request, CancellationToken token) =>
        Ok(await service.CleanupAsync(request, token));

    [HttpPost("probe")]
    public async Task<IActionResult> Probe([FromBody] TeamLabProbeRequest request, CancellationToken token) =>
        Ok(await service.ProbeAsync(request, token));

    [HttpPost("containers/attach")]
    public async Task<IActionResult> AttachContainer([FromBody] TeamLabContainerAttachRequest request,
        CancellationToken token) =>
        Ok(await service.AttachContainerAsync(request, token));

    [HttpPost("dhcp-dns")]
    public async Task<IActionResult> ConfigureDhcpDns([FromBody] TeamLabDhcpDnsRequest request,
        CancellationToken token) =>
        Ok(await service.ConfigureDhcpDnsAsync(request, token));

    [HttpPost("dhcp-dns/probe")]
    public async Task<IActionResult> ProbeDhcpDns([FromBody] TeamLabDhcpDnsProbeRequest request,
        CancellationToken token) =>
        Ok(await service.ProbeDhcpDnsAsync(request, token));

    [HttpPost("fabric/apply")]
    public async Task<IActionResult> ApplyFabric([FromBody] TeamLabFabricApplyRequest request,
        CancellationToken token) =>
        Ok(await service.ApplyFabricAsync(request, token));

    [HttpPost("capture/start")]
    public async Task<IActionResult> StartCapture([FromBody] TeamLabCaptureStartRequest request,
        CancellationToken token) =>
        Ok(await service.StartCaptureAsync(request, token));

    [HttpPost("capture/stop")]
    public async Task<IActionResult> StopCapture([FromBody] TeamLabCaptureStopRequest request,
        CancellationToken token) =>
        Ok(await service.StopCaptureAsync(request, token));

    [HttpPost("capture/status")]
    public async Task<IActionResult> CaptureStatus([FromBody] TeamLabCaptureStatusRequest request,
        CancellationToken token) =>
        Ok(await service.GetCaptureStatusAsync(request));

    [HttpGet("capture/{runtimeId:int}/{jobId:int}/download")]
    public IActionResult DownloadCapture(int runtimeId, int jobId)
    {
        string path;
        try
        {
            path = TeamLabNetworkService.ResolveCaptureFilePath(runtimeId, jobId);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        if (!System.IO.File.Exists(path))
            return NotFound(new { message = "TeamLab capture file was not found." });

        return PhysicalFile(path, "application/vnd.tcpdump.pcap", $"teamlab-capture-{runtimeId}-{jobId}.pcap",
            enableRangeProcessing: true);
    }

    [HttpPost("flows/start")]
    public async Task<IActionResult> StartFlowMetadata([FromBody] TeamLabFlowStartRequest request,
        CancellationToken token) =>
        Ok(await service.StartFlowMetadataAsync(request, token));

    [HttpPost("flows/stop")]
    public async Task<IActionResult> StopFlowMetadata([FromBody] TeamLabFlowStopRequest request,
        CancellationToken token) =>
        Ok(await service.StopFlowMetadataAsync(request, token));

    [HttpPost("flows/snapshot")]
    public async Task<IActionResult> FlowMetadataSnapshot([FromBody] TeamLabFlowSnapshotRequest request,
        CancellationToken token) =>
        Ok(await service.GetFlowMetadataSnapshotAsync(request, token));
}
