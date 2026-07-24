using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Application;

public sealed class PenetrationTeamLabLifecycleObserver(AppDbContext context)
    : IRuntimeTicketLifecycleObserver
{
    public async Task ProjectAsync(
        DeploymentQueueTicket ticket,
        CancellationToken cancellationToken)
    {
        if (ticket.Kind != DeploymentQueueKind.TeamLabRuntime)
            return;

        if (ticket.Operation == RuntimeOperationKind.Reset && ticket.ApiOperationId is { } resetOperationId)
            await ProjectResetAsync(ticket, resetOperationId, cancellationToken);

        if (ticket.Operation == RuntimeOperationKind.Destroy && ticket.ApiOperationId is { } destroyOperationId)
            await ProjectDestroyAsync(ticket, destroyOperationId, cancellationToken);
    }

    async Task ProjectResetAsync(
        DeploymentQueueTicket ticket,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var record = await context.PenetrationResetRecords.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken);
        if (record is null)
            return;

        switch (ticket.Status)
        {
            case DeploymentQueueTicketStatus.Running:
                record.Status = PenetrationResetStatus.Running;
                break;
            case DeploymentQueueTicketStatus.Succeeded:
                record.Status = PenetrationResetStatus.Succeeded;
                record.FailureClass = PenetrationResetFailureClass.None;
                record.CompletedAt = ticket.CompletedAt ?? DateTimeOffset.UtcNow;
                break;
            case DeploymentQueueTicketStatus.Failed:
                record.Status = PenetrationResetStatus.Failed;
                record.FailureClass = IsScenarioFailure(ticket.ErrorCategory)
                    ? PenetrationResetFailureClass.Scenario
                    : PenetrationResetFailureClass.Infrastructure;
                record.CompletedAt = ticket.CompletedAt ?? DateTimeOffset.UtcNow;
                break;
            case DeploymentQueueTicketStatus.Cancelled:
                record.Status = PenetrationResetStatus.Cancelled;
                record.FailureClass = PenetrationResetFailureClass.Infrastructure;
                record.CompletedAt = ticket.CompletedAt ?? DateTimeOffset.UtcNow;
                break;
        }
    }

    async Task ProjectDestroyAsync(
        DeploymentQueueTicket ticket,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationTeamRuntimeBindings.SingleOrDefaultAsync(
            item => item.DestroyOperationId == operationId, cancellationToken);
        if (binding is null || ticket.Status != DeploymentQueueTicketStatus.Succeeded)
            return;

        var physicallyDestroyed = await context.TeamLabRuntimes.AsNoTracking()
            .AnyAsync(item => item.Id == binding.RuntimeId && item.Status == TeamLabRuntimeStatus.Destroyed,
                cancellationToken);
        if (!physicallyDestroyed)
            return;

        binding.Status = PenetrationRuntimeBindingStatus.Destroyed;
        binding.DestroyedAt = ticket.CompletedAt ?? DateTimeOffset.UtcNow;
    }

    static bool IsScenarioFailure(OperationalErrorCategory? category) => category is
        OperationalErrorCategory.Validation or
        OperationalErrorCategory.Conflict;
}
