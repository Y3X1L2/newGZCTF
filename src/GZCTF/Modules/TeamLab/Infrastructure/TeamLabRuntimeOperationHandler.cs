using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabRuntimeOperationHandler(
    AppDbContext context,
    ITeamLabRuntimeApplicationService runtimes,
    ITeamLabTopologyApplicationService topologies,
    TeamLabAuthorizationService authorization,
    TeamLabAccessGrantService access,
    TeamLabTrafficApplicationService traffic,
    TeamLabRuntimeOperationPayloadProtector protector,
    ApiOperationService operations) : IApiOperationHandler
{
    public string Kind => TeamLabRuntimeOperationApplicationService.OperationKind;

    public async Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        var job = await context.TeamLabRuntimeOperationJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken)
            ?? throw new ApiOperationTerminalException("teamlab_job_not_found", "The TeamLab operation job was not found.");
        if (job.ResultJson is not null) return;
        var operation = await context.ApiOperations.AsNoTracking().SingleAsync(item => item.Id == operationId, cancellationToken);

        if (job.Kind is >= TeamLabRuntimeOperationKind.TopologyCreate and <= TeamLabRuntimeOperationKind.CaptureStop)
        {
            await ExecuteExternalCommandAsync(job, operation, leaseOwner, cancellationToken);
            return;
        }

        if (job.Kind == TeamLabRuntimeOperationKind.Destroy)
        {
            var payload = ReadPayload(job);
            var runtimeId = payload.RuntimeId ?? job.RuntimePublicId
                ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "Destroy runtime ID is missing.");
            await operations.UpdateProgressAsync(operationId, leaseOwner, "runtime-destroying", 0, 1,
                "teamlab-runtime", runtimeId.ToString("D"), null, cancellationToken);
            var queued = await runtimes.DestroyAndEnqueueAsync(
                runtimeId, operationId, operation.ActorUserId, cancellationToken);
            job.RuntimeId = await context.TeamLabRuntimes.AsNoTracking()
                .Where(runtime => runtime.PublicId == runtimeId)
                .Select(runtime => (int?)runtime.Id)
                .SingleAsync(cancellationToken);
            job.RuntimePublicId = runtimeId;
            job.ProtectedPayload = null;
            await context.SaveChangesAsync(cancellationToken);
            await WaitForTicketAsync(job, queued.TicketId, operationId, leaseOwner, cancellationToken);
            return;
        }

        if (job.RuntimeId is null)
        {
            var linkedRuntimeId = await context.DeploymentQueueTickets.AsNoTracking()
                .Where(ticket => ticket.ApiOperationId == operationId && ticket.TeamLabRuntimeId != null)
                .Select(ticket => ticket.TeamLabRuntimeId)
                .FirstOrDefaultAsync(cancellationToken);
            if (linkedRuntimeId is not null)
            {
                job.RuntimeId = linkedRuntimeId;
                job.RuntimePublicId = await context.TeamLabRuntimes.AsNoTracking()
                    .Where(item => item.Id == linkedRuntimeId)
                    .Select(item => (Guid?)item.PublicId)
                    .SingleAsync(cancellationToken);
            }
            else
            {
                var payload = ReadPayload(job);
                TeamLabRuntimeCreateResult result;
                if (job.Kind == TeamLabRuntimeOperationKind.Create)
                {
                    result = await runtimes.PlanAndEnqueueAsync(
                        payload.Create ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "Create payload is missing."),
                        operation.ActorUserId ?? throw new ApiOperationTerminalException("authentication_required", "The operation actor is missing."),
                        operation.ActorUserId ?? throw new ApiOperationTerminalException("authentication_required", "The operation actor is missing."),
                        operation.RequestHash,
                        operationId,
                        payload.Create?.ExternalReference,
                        cancellationToken);
                }
                else if (job.Kind == TeamLabRuntimeOperationKind.Reset)
                {
                    result = await runtimes.ResetAndEnqueueAsync(
                        payload.RuntimeId ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "Reset runtime ID is missing."),
                        payload.Reset ?? new ResetTeamLabRuntimeModel(null),
                        operationId,
                        cancellationToken);
                }
                else
                {
                    throw new ApiOperationTerminalException("teamlab_operation_invalid", "The TeamLab operation kind is invalid.");
                }
                job.RuntimeId = result.RuntimeId;
                job.RuntimePublicId = result.RuntimePublicId;
            }
            job.ProtectedPayload = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        var ticket = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(item => item.TeamLabRuntimeId == job.RuntimeId && item.ApiOperationId == operationId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The TeamLab operation has no deployment queue ticket.");
        await WaitForTicketAsync(job, ticket.Id, operationId, leaseOwner, cancellationToken);
    }

    private async Task ExecuteExternalCommandAsync(
        TeamLabRuntimeOperationJob job,
        ApiOperation operation,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var actorUserId = operation.ActorUserId
            ?? throw new ApiOperationTerminalException("authentication_required", "The operation actor is missing.");
        var isAdministrator = await context.Users.AsNoTracking()
            .Where(item => item.Id == actorUserId)
            .Select(item => item.Role >= Role.Admin)
            .SingleOrDefaultAsync(cancellationToken);
        var payload = ReadPayload(job);

        switch (job.Kind)
        {
            case TeamLabRuntimeOperationKind.TopologyCreate:
            {
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "topology-creating", 0, 1,
                    "teamlab-topology", null, null, cancellationToken);
                var result = await topologies.CreateForOperationAsync(
                    payload.CreateTopology ?? throw MissingPayload("topology create"),
                    actorUserId, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "topology-created", 1, 1,
                    "teamlab-topology", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.TopologyUpdate:
            {
                var topologyId = payload.TopologyId ?? throw MissingPayload("topology update ID");
                var result = await topologies.UpdateForOperationAsync(
                    topologyId,
                    payload.UpdateTopology ?? throw MissingPayload("topology update"),
                    actorUserId, isAdministrator, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "topology-updated", 1, 1,
                    "teamlab-topology", topologyId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.TopologyDelete:
            {
                var topologyId = payload.TopologyId ?? throw MissingPayload("topology delete ID");
                await topologies.DeleteForOperationAsync(
                    topologyId, actorUserId, isAdministrator, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "topology-deleted", 1, 1,
                    "teamlab-topology", topologyId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, new { topologyId, deleted = true }, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.TopologyPublish:
            {
                var topologyId = payload.TopologyId ?? throw MissingPayload("topology publish ID");
                var result = await topologies.PublishForOperationAsync(
                    topologyId,
                    payload.PublishTopology?.Revision ?? throw MissingPayload("topology publish revision"),
                    actorUserId, isAdministrator, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "release-published", 1, 1,
                    "teamlab-release", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.AccessGrantCreate:
            {
                var runtimeId = payload.RuntimeId ?? throw MissingPayload("runtime ID");
                if (!string.Equals(payload.CreateAccessGrant?.Type, "WireGuard", StringComparison.OrdinalIgnoreCase))
                    throw new ApiOperationTerminalException(
                        "topology_invalid", "Only WireGuard access grants are supported.");
                await authorization.RequireRuntimeOwnerAsync(runtimeId, actorUserId, isAdministrator, cancellationToken);
                var result = await access.CreateForOperationAsync(runtimeId, operation.Id, cancellationToken);
                job.RuntimePublicId = runtimeId;
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "access-grant-created", 1, 1,
                    "teamlab-access-grant", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, new { grantId = result.Id }, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.AccessGrantRevoke:
            {
                var runtimeId = payload.RuntimeId ?? throw MissingPayload("runtime ID");
                var grantId = payload.AccessGrantId ?? throw MissingPayload("access grant ID");
                await authorization.RequireRuntimeOwnerAsync(runtimeId, actorUserId, isAdministrator, cancellationToken);
                await access.RevokeAsync(runtimeId, grantId, cancellationToken);
                job.RuntimePublicId = runtimeId;
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "access-grant-revoked", 1, 1,
                    "teamlab-access-grant", grantId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, new { grantId, revoked = true }, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.CaptureStart:
            {
                var runtimeId = payload.RuntimeId ?? throw MissingPayload("runtime ID");
                await authorization.RequireRuntimeOwnerAsync(runtimeId, actorUserId, isAdministrator, cancellationToken);
                var result = await traffic.StartCaptureForOperationAsync(
                    runtimeId,
                    payload.CreateCapture ?? throw MissingPayload("capture request"),
                    operation.Id,
                    cancellationToken);
                job.RuntimePublicId = runtimeId;
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "capture-started", 1, 1,
                    "teamlab-capture", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.CaptureStop:
            {
                var runtimeId = payload.RuntimeId ?? throw MissingPayload("runtime ID");
                var captureId = payload.CaptureId ?? throw MissingPayload("capture ID");
                await authorization.RequireRuntimeOwnerAsync(runtimeId, actorUserId, isAdministrator, cancellationToken);
                var result = await traffic.StopCaptureAsync(runtimeId, captureId, cancellationToken);
                job.RuntimePublicId = runtimeId;
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "capture-stopped", 1, 1,
                    "teamlab-capture", captureId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            default:
                throw new ApiOperationTerminalException(
                    "teamlab_operation_invalid", "The TeamLab operation kind is invalid.");
        }
    }

    private static ApiOperationTerminalException MissingPayload(string field) =>
        new("teamlab_payload_invalid", $"The TeamLab operation {field} is missing.");

    public async Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var job = await context.TeamLabRuntimeOperationJobs.SingleOrDefaultAsync(item => item.OperationId == operationId, cancellationToken);
        if (job is null) return;
        job.ProtectedPayload = null;
        if (job.Kind == TeamLabRuntimeOperationKind.AccessGrantCreate)
        {
            var grant = await context.TeamLabAccessGrants.SingleOrDefaultAsync(
                item => item.ApiOperationId == operationId, cancellationToken);
            if (grant is not null)
            {
                grant.ProtectedDownloadToken = null;
                grant.Revoked = true;
                grant.RevokedAt ??= DateTimeOffset.UtcNow;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task WaitForTicketAsync(
        TeamLabRuntimeOperationJob job,
        Guid ticketId,
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            context.ChangeTracker.Clear();
            var ticket = await context.DeploymentQueueTickets.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == ticketId, cancellationToken)
                ?? throw new InvalidOperationException("The TeamLab deployment queue ticket was deleted.");
            var (stage, progress) = ticket.Status switch
            {
                DeploymentQueueTicketStatus.Pending => ("runtime-queued", 1L),
                DeploymentQueueTicketStatus.Scheduling or DeploymentQueueTicketStatus.Scheduled =>
                    ("runtime-assigned", 2L),
                DeploymentQueueTicketStatus.Running => ("runtime-deploying", 3L),
                DeploymentQueueTicketStatus.Succeeded => ("runtime-ready", 4L),
                DeploymentQueueTicketStatus.Failed => ("runtime-failed", 4L),
                DeploymentQueueTicketStatus.Cancelled => ("runtime-cancelled", 4L),
                _ => ("runtime-queued", 1L)
            };
            await operations.UpdateProgressAsync(operationId, leaseOwner, stage, progress, 4,
                "teamlab-runtime", job.RuntimePublicId?.ToString("D"), ticket.Id, cancellationToken);
            if (ticket.Status == DeploymentQueueTicketStatus.Succeeded)
            {
                var projection = (await runtimes.GetAsync(job.RuntimePublicId!.Value, cancellationToken)).ToOpen();
                var trackedJob = await context.TeamLabRuntimeOperationJobs.SingleAsync(item => item.OperationId == operationId, cancellationToken);
                await CompleteJobAsync(trackedJob, projection, cancellationToken);
                return;
            }
            if (ticket.Status is DeploymentQueueTicketStatus.Failed or DeploymentQueueTicketStatus.Cancelled)
                throw new ApiOperationTerminalException(
                    ticket.Status == DeploymentQueueTicketStatus.Cancelled ? "operation_cancelled" : "operation_failed",
                    ticket.Status == DeploymentQueueTicketStatus.Cancelled
                        ? "The TeamLab deployment was cancelled."
                        : "The TeamLab deployment failed. Use the operation ID to inspect administrator diagnostics.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private TeamLabRuntimeOperationPayload ReadPayload(TeamLabRuntimeOperationJob job)
    {
        if (string.IsNullOrWhiteSpace(job.ProtectedPayload))
            throw new ApiOperationTerminalException("teamlab_payload_missing", "The TeamLab operation payload is unavailable.");
        return protector.Unprotect(job.ProtectedPayload);
    }

    private async Task CompleteJobAsync<T>(TeamLabRuntimeOperationJob job, T result, CancellationToken cancellationToken)
    {
        job.ResultJson = JsonSerializer.Serialize(result);
        job.ProtectedPayload = null;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
