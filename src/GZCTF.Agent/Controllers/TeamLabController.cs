using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Observation;
using GZCTF.Agent.Services.RuntimeSignals;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Agent.Services.Vm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GZCTF.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts.Execution;
using System.Net.NetworkInformation;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/teamlab")]
public class TeamLabController(
    TeamLabNetworkService service,
    TeamLabPacketObserver observer,
    TeamLabPcapService pcap,
    TeamLabContainerNetworkFinalizeService containerNetworkFinalize,
    AgentRuntimeSignalJournal runtimeSignals,
    DockerService docker,
    KvmService kvm,
    LibvirtTeamLabProvider libvirt,
    AgentOperationGate gate,
    TeamLabExecutionPlanExecutor executionPlans,
    TeamLabLinkPolicyService linkPolicies,
    TeamLabServiceAccessService serviceAccess,
    IOptions<AgentTeamLabConfig> teamLabOptions) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken token) => Ok(await service.GetStatusAsync(token));

    [HttpGet("interfaces")]
    public IReadOnlyList<TeamLabHostInterface> Interfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(item => item.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 &&
                       !item.Name.StartsWith("veth", StringComparison.Ordinal) &&
                       !item.Name.StartsWith("br-", StringComparison.Ordinal) &&
                       !item.Name.StartsWith("virbr", StringComparison.Ordinal) &&
                       !item.Name.StartsWith("docker", StringComparison.Ordinal) &&
                       !item.Name.StartsWith("gz", StringComparison.Ordinal))
        .OrderBy(item => item.Name, StringComparer.Ordinal)
        .Select(item => new TeamLabHostInterface(
            item.Name,
            item.GetPhysicalAddress().ToString().Chunk(2).Select(chars => new string(chars)).Aggregate(string.Empty,
                (current, part) => current.Length == 0 ? part : current + ":" + part).ToLowerInvariant(),
            item.OperationalStatus == OperationalStatus.Up,
            item.GetIPProperties().UnicastAddresses.Select(address => address.Address.ToString()).ToArray()))
        .ToArray();

    [HttpPost("execution-plan/apply")]
    public async Task<IActionResult> ApplyExecutionPlan(
        [FromBody] TeamLabExecutionPlanApplyRequest? request,
        CancellationToken token)
    {
        if (teamLabOptions.Value.ExecutionModel != TeamLabExecutionModel.V2)
            return NotFound();
        if (request?.Plan is null) return BadRequest("Execution plan request is required.");
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabExecution, token);
        return Ok(await executionPlans.ApplyAsync(request.Plan, token));
    }

    [HttpPost("execution-plan/asset-control")]
    public async Task<TeamLabAssetControlResult> ControlAsset(TeamLabAssetControlRequest request, CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return await executionPlans.ControlAssetAsync(request, token);
    }

    [HttpPost("execution-plan/device-probe")]
    public Task<TeamLabDeviceObservation> ProbeDevice(TeamLabDeviceProbeRequest request, CancellationToken token) =>
        executionPlans.ProbeDeviceAsync(request, token);

    [HttpPost("execution-plan/cleanup")]
    public async Task<IActionResult> CleanupExecutionPlan(
        [FromBody] TeamLabExecutionPlanCleanupRequest? request,
        CancellationToken token)
    {
        if (teamLabOptions.Value.ExecutionModel != TeamLabExecutionModel.V2)
            return NotFound();
        if (request?.Plan is null) return BadRequest("Execution plan request is required.");
        // Cleanup is a bounded, identity-fenced operation. It must not wait behind an unrelated
        // long-running apply; the per-plan executor lease still serializes the same shard.
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await executionPlans.CleanupAsync(request.Plan, token));
    }

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

    [HttpPost("assets/pause")]
    public Task<TeamLabAssetLifecycleResponse> PauseAsset(
        [FromBody] TeamLabAssetLifecycleRequest request,
        CancellationToken token) =>
        ChangeAssetLifecycleAsync(request, pause: true, token);

    [HttpPost("assets/resume")]
    public Task<TeamLabAssetLifecycleResponse> ResumeAsset(
        [FromBody] TeamLabAssetLifecycleRequest request,
        CancellationToken token) =>
        ChangeAssetLifecycleAsync(request, pause: false, token);

    [HttpPost("assets/pause-batch")]
    public Task<TeamLabAssetLifecycleBatchResult[]> PauseAssets(
        [FromBody] TeamLabAssetLifecycleBatchRequest request,
        CancellationToken token) => ChangeAssetLifecycleBatchAsync(request, pause: true, token);

    [HttpPost("assets/resume-batch")]
    public Task<TeamLabAssetLifecycleBatchResult[]> ResumeAssets(
        [FromBody] TeamLabAssetLifecycleBatchRequest request,
        CancellationToken token) => ChangeAssetLifecycleBatchAsync(request, pause: false, token);

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

    [HttpPost("capture/start-batch")]
    public async Task<IActionResult> StartCaptures([FromBody] TeamLabCaptureStartRequest[] requests,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        var results = new TeamLabCaptureResponse[requests.Length];
        for (var index = 0; index < requests.Length; index++)
            results[index] = await pcap.StartAsync(requests[index], token);
        return Ok(results);
    }

    [HttpPost("capture/stop")]
    public async Task<IActionResult> StopCapture([FromBody] TeamLabCaptureStopRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await pcap.StopAsync(request, token));
    }

    [HttpPost("capture/stop-batch")]
    public async Task<IActionResult> StopCaptures([FromBody] TeamLabCaptureStopRequest[] requests,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        var results = new TeamLabCaptureResponse[requests.Length];
        for (var index = 0; index < requests.Length; index++)
            results[index] = await pcap.StopAsync(requests[index], token);
        return Ok(results);
    }

    [HttpPost("capture/status")]
    public async Task<IActionResult> CaptureStatus([FromBody] TeamLabCaptureStatusRequest request,
        CancellationToken token) =>
        Ok(await pcap.StatusAsync(request, token));

    [HttpPost("capture/status-batch")]
    public async Task<IActionResult> CaptureStatuses([FromBody] TeamLabCaptureStatusRequest[] requests,
        CancellationToken token)
    {
        var results = new TeamLabCaptureResponse[requests.Length];
        for (var index = 0; index < requests.Length; index++)
            results[index] = await pcap.StatusAsync(requests[index], token);
        return Ok(results);
    }

    [HttpPost("capture/upload")]
    public async Task<IActionResult> UploadCapture(
        [FromBody] TeamLabCaptureUploadRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await pcap.UploadAsync(request, token));
    }

    [HttpPost("capture/upload-batch")]
    public async Task<IActionResult> UploadCaptures(
        [FromBody] TeamLabCaptureUploadRequest[] requests,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        var results = new TeamLabCaptureResponse[requests.Length];
        for (var index = 0; index < requests.Length; index++)
            results[index] = await pcap.UploadAsync(requests[index], token);
        return Ok(results);
    }

    [HttpPost("capture/delete")]
    public async Task<IActionResult> DeleteCapture(
        [FromBody] TeamLabCaptureDeleteRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        return Ok(await pcap.DeleteAsync(request, token));
    }

    [HttpPost("capture/delete-batch")]
    public async Task<IActionResult> DeleteCaptures(
        [FromBody] TeamLabCaptureDeleteRequest[] requests,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        var results = new TeamLabCaptureResponse[requests.Length];
        for (var index = 0; index < requests.Length; index++)
            results[index] = await pcap.DeleteAsync(requests[index], token);
        return Ok(results);
    }

    [HttpPost("observations/read")]
    public async Task<IActionResult> ReadObservations([FromBody] TeamLabObservationBatchRequest request,
        CancellationToken token)
    {
        if (request.RuntimeId <= 0 || request.Generation <= 0 || request.AfterSequence < 0 ||
            request.AcknowledgeThroughSequence < 0)
            return BadRequest("Invalid TeamLab observation cursor.");
        if (request.AcknowledgeThroughSequence > request.AfterSequence)
            return BadRequest("Observation acknowledgement cannot exceed the read cursor.");
        if (request.AcknowledgeThroughSequence > 0)
            await observer.AcknowledgeAsync(
                request.RuntimeId, request.Generation, request.AcknowledgeThroughSequence, token);
        return Ok(observer.Read(request));
    }

    [HttpPost("link-policy/apply")]
    public async Task<IActionResult> ApplyLinkPolicy(
        [FromBody] TeamLabLinkPolicyApplyRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        return Ok(await linkPolicies.ApplyAsync(request, token));
    }

    [HttpPost("link-policy/apply-batch")]
    public async Task<IActionResult> ApplyLinkPolicies(
        [FromBody] TeamLabLinkPolicyApplyRequest[] requests,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        var results = new TeamLabLinkPolicyResponse[requests.Length];
        for (var index = 0; index < requests.Length; index++)
            results[index] = await linkPolicies.ApplyAsync(requests[index], token);
        return Ok(results);
    }

    [HttpPost("link-policy/recover")]
    public async Task<IActionResult> RecoverLinkPolicy(
        [FromBody] TeamLabLinkPolicyRecoverRequest request,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        return Ok(await linkPolicies.RecoverAsync(request, token));
    }

    [HttpPost("link-policy/recover-batch")]
    public async Task<IActionResult> RecoverLinkPolicies(
        [FromBody] TeamLabLinkPolicyRecoverRequest[] requests,
        CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        var results = new TeamLabLinkPolicyResponse[requests.Length];
        for (var index = 0; index < requests.Length; index++)
            results[index] = await linkPolicies.RecoverAsync(requests[index], token);
        return Ok(results);
    }

    [HttpPost("service-access/apply")]
    public async Task<IActionResult> ApplyServiceAccess(TeamLabServiceForwardRequest request, CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.TeamLabNetwork, token);
        await serviceAccess.ApplyAsync(request, token);
        return Ok();
    }

    [HttpPost("service-access/remove")]
    public async Task<IActionResult> RemoveServiceAccess(TeamLabServiceForwardRequest request, CancellationToken token)
    {
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        await serviceAccess.RemoveAsync(request, token);
        return Ok();
    }

    private async Task<TeamLabAssetLifecycleResponse> ChangeAssetLifecycleAsync(
        TeamLabAssetLifecycleRequest request,
        bool pause,
        CancellationToken token)
    {
        if (request.Generation < 1 || string.IsNullOrWhiteSpace(request.ResourceId))
            throw new AgentOperationException(
                "Validation", "runtime.lifecycle_invalid", "Asset lifecycle request is invalid.", false,
                StatusCodes.Status422UnprocessableEntity);
        await using var permit = await gate.EnterAsync(AgentOperationCategory.Control, token);
        if (!request.DryRun)
        {
            switch (request.Kind.Trim().ToLowerInvariant())
            {
                case "docker":
                    if (pause)
                        await docker.PauseContainerAsync(request.ResourceId, request.Generation, token);
                    else
                        await docker.ResumeContainerAsync(request.ResourceId, request.Generation, token);
                    break;
                case "vm":
                    if (request.ExecutionModel == TeamLabExecutionModel.V2)
                    {
                        var result = pause
                            ? await libvirt.PauseAsync(request.ResourceId, request.Generation, token)
                            : await libvirt.ResumeAsync(request.ResourceId, request.Generation, token);
                        if (!result.Success)
                            throw new AgentOperationException(
                                "Compute", "runtime.vm_lifecycle_failed", result.State, false,
                                StatusCodes.Status409Conflict);
                    }
                    else if (pause)
                        await kvm.SuspendVmAsync(request.ResourceId, request.Generation, token);
                    else
                        await kvm.ResumeVmAsync(request.ResourceId, request.Generation, token);
                    break;
                default:
                    throw new AgentOperationException(
                        "Validation", "runtime.lifecycle_kind_unsupported", "Asset kind is not supported.", false,
                        StatusCodes.Status422UnprocessableEntity);
            }
        }
        return new TeamLabAssetLifecycleResponse(
            true, request.DryRun, pause ? "paused" : "running",
            request.DryRun ? "Asset lifecycle command validated." : "Asset lifecycle command completed.");
    }

    private async Task<TeamLabAssetLifecycleBatchResult[]> ChangeAssetLifecycleBatchAsync(
        TeamLabAssetLifecycleBatchRequest request,
        bool pause,
        CancellationToken token)
    {
        if (request.Generation < 1 || request.Assets.Length is < 1 or > 256 ||
            request.Assets.Any(item => item.AssetId <= 0 || string.IsNullOrWhiteSpace(item.ResourceId)))
            throw new AgentOperationException(
                "Validation", "runtime.lifecycle_invalid", "Asset lifecycle batch is invalid.", false,
                StatusCodes.Status422UnprocessableEntity);
        var results = new TeamLabAssetLifecycleBatchResult[request.Assets.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, request.Assets.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = 32,
            CancellationToken = token
        }, async (index, cancellationToken) =>
        {
            var item = request.Assets[index];
            try
            {
                var result = await ChangeAssetLifecycleAsync(new TeamLabAssetLifecycleRequest(
                    item.Kind, item.ResourceId, request.Generation, request.DryRun, request.ExecutionModel),
                    pause, cancellationToken);
                results[index] = new TeamLabAssetLifecycleBatchResult(item.AssetId, result.Success, result.Message);
            }
            catch (AgentOperationException exception)
            {
                results[index] = new TeamLabAssetLifecycleBatchResult(item.AssetId, false, exception.Message);
            }
        });
        return results;
    }
}
