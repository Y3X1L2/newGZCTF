using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabRuntimeOperationHandler(
    AppDbContext context,
    ITeamLabRuntimeApplicationService runtimes,
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

        if (job.Kind == TeamLabRuntimeOperationKind.Destroy)
        {
            var payload = ReadPayload(job);
            var runtimeId = payload.RuntimeId ?? job.RuntimePublicId
                ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "Destroy runtime ID is missing.");
            await operations.UpdateProgressAsync(operationId, leaseOwner, "runtime-destroying", 0, 1,
                "teamlab-runtime", runtimeId.ToString("D"), null, cancellationToken);
            var projection = await runtimes.DestroyAsync(runtimeId, cancellationToken);
            await CompleteJobAsync(job, projection, cancellationToken);
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

    public async Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var job = await context.TeamLabRuntimeOperationJobs.SingleOrDefaultAsync(item => item.OperationId == operationId, cancellationToken);
        if (job is null) return;
        job.ProtectedPayload = null;
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
                DeploymentQueueTicketStatus.Assigned => ("runtime-assigned", 2L),
                DeploymentQueueTicketStatus.Creating => ("runtime-deploying", 3L),
                DeploymentQueueTicketStatus.Completed => ("runtime-ready", 4L),
                DeploymentQueueTicketStatus.Failed => ("runtime-failed", 4L),
                DeploymentQueueTicketStatus.Cancelled => ("runtime-cancelled", 4L),
                _ => ("runtime-queued", 1L)
            };
            await operations.UpdateProgressAsync(operationId, leaseOwner, stage, progress, 4,
                "teamlab-runtime", job.RuntimePublicId?.ToString("D"), ticket.Id, cancellationToken);
            if (ticket.Status == DeploymentQueueTicketStatus.Completed)
            {
                var projection = await runtimes.GetAsync(job.RuntimePublicId!.Value, cancellationToken);
                var trackedJob = await context.TeamLabRuntimeOperationJobs.SingleAsync(item => item.OperationId == operationId, cancellationToken);
                await CompleteJobAsync(trackedJob, projection, cancellationToken);
                return;
            }
            if (ticket.Status is DeploymentQueueTicketStatus.Failed or DeploymentQueueTicketStatus.Cancelled)
                throw new ApiOperationTerminalException(
                    ticket.Status == DeploymentQueueTicketStatus.Cancelled ? "operation_cancelled" : "operation_failed",
                    ticket.ErrorMessage ?? "The TeamLab deployment failed.");
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
