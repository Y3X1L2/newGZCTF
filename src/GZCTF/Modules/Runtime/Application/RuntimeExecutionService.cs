using System.Diagnostics;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Application;

public sealed class RuntimeExecutionService(
    IServiceScopeFactory scopeFactory,
    NodeDispatchLimiter dispatchLimiter,
    ILogger<RuntimeExecutionService> logger)
{
    const int BatchSize = 64;
    static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(10);

    public async Task<int> ExecuteScheduledAsync(CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.Status == DeploymentQueueTicketStatus.Scheduled)
            .OrderBy(ticket => ticket.Operation == RuntimeOperationKind.Create ? 1 : 0)
            .ThenBy(ticket => ticket.CreatedAt)
            .ThenBy(ticket => ticket.Id)
            .Take(BatchSize)
            .Select(ticket => ticket.Id)
            .ToListAsync(token);

        if (ids.Count == 0)
            return 0;

        var results = await Task.WhenAll(ids.Select(id => ExecuteTicketAsync(id, token)));
        return results.Count(result => result);
    }

    public async Task<bool> ExecuteTicketAsync(Guid ticketId, CancellationToken token)
    {
        var claimedAt = DateTimeOffset.UtcNow;
        var claimOwner = $"executor:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        if (!await TryClaimAsync(ticketId, claimedAt, claimOwner, token))
            return false;

        try
        {
            await ExecuteClaimedTicketAsync(ticketId, claimOwner, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Runtime ticket {TicketId} failed during execution.", ticketId);
            await FailClaimedTicketAsync(ticketId, claimOwner, ex, token);
        }

        return true;
    }

    async Task<bool> TryClaimAsync(Guid ticketId, DateTimeOffset now, string claimOwner,
        CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<IOperationalEventWriter>();
        if (context.Database.IsRelational())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            var claimed = await context.DeploymentQueueTickets
                .Where(ticket => ticket.Id == ticketId && ticket.Status == DeploymentQueueTicketStatus.Scheduled)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(ticket => ticket.Status, DeploymentQueueTicketStatus.Running)
                    .SetProperty(ticket => ticket.ClaimOwner, claimOwner)
                    .SetProperty(ticket => ticket.ClaimExpiresAt, now.Add(ClaimTimeout))
                    .SetProperty(ticket => ticket.StartedAt, now), token) == 1;
            if (!claimed)
            {
                await transaction.RollbackAsync(token);
                return false;
            }
            var claimedTicket = await context.DeploymentQueueTickets.SingleAsync(
                ticket => ticket.Id == ticketId, token);
            AppendExecutionStarted(events, claimedTicket);
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return true;
        }

        var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket?.Status != DeploymentQueueTicketStatus.Scheduled)
            return false;
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.ClaimOwner = claimOwner;
        ticket.ClaimExpiresAt = now.Add(ClaimTimeout);
        ticket.StartedAt = now;
        AppendExecutionStarted(events, ticket);
        await context.SaveChangesAsync(token);
        return true;
    }

    async Task ExecuteClaimedTicketAsync(Guid ticketId, string claimOwner, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
        var executor = scope.ServiceProvider.GetRequiredService<DeploymentExecutionService>();
        var events = scope.ServiceProvider.GetRequiredService<IOperationalEventWriter>();
        var correlation = scope.ServiceProvider.GetRequiredService<OperationalCorrelation>();
        var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket is null || ticket.Status != DeploymentQueueTicketStatus.Running ||
            !string.Equals(ticket.ClaimOwner, claimOwner, StringComparison.Ordinal))
            return;
        using var correlationScope = correlation.Begin(ticket.Id);
        using var activity = RuntimeOperationalEvents.StartActivity(ticket, "runtime.execute");
        var executionStartedAt = Stopwatch.GetTimestamp();

        if (ticket.TargetNodeId is not { } nodeId)
        {
            var error = new OperationalError(
                OperationalErrorCategory.NodeUnavailable,
                OperationalErrorCodes.NodeNotFound,
                "Scheduled ticket has no target node.",
                false,
                Operation: "runtime.execute");
            await MarkFailedAsync(context, capacity, events, ticket, error, token);
            await context.SaveChangesAsync(token);
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            return;
        }

        logger.SystemLog(
            $"Deployment execution started: ticket={ticket.Id}, kind={ticket.Kind}, operation={ticket.Operation}, node={nodeId}.",
            TaskStatus.Pending, LogLevel.Information);

        var manifest = await context.WorkerNodes.AsNoTracking()
            .Where(item => item.Id == nodeId)
            .Select(item => item.CapabilityManifestJson)
            .SingleOrDefaultAsync(token);
        var category = ResolveCategory(ticket);
        var limit = ResolveLimit(AgentCapabilityEvaluator.Parse(manifest), category);
        if (ticket.Operation != RuntimeOperationKind.Create)
        {
            ticket.Stage = ticket.Operation switch
            {
                RuntimeOperationKind.Extend => DeploymentStage.Extending,
                RuntimeOperationKind.Stop => DeploymentStage.Stopping,
                RuntimeOperationKind.Reset => DeploymentStage.RollingBack,
                RuntimeOperationKind.Destroy => DeploymentStage.Destroying,
                _ => DeploymentStage.NodeExecutionWaiting
            };
            ticket.StageMessage = $"Executing {ticket.Operation} control operation.";
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                RuntimeOperationalEvents.ControlStartedCode(ticket.Operation),
                OperationalEventOutcome.Started,
                "Runtime control operation started."));
            await context.SaveChangesAsync(token);
        }
        DeploymentExecutionResult? result = null;
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var renewalTask = RenewClaimLoopAsync(ticketId, claimOwner, executionCancellation, token);
        try
        {
            await dispatchLimiter.RunAsync(nodeId, category, limit, async operationToken =>
            {
                result = await executor.ExecuteAsync(ticket, operationToken);
            }, executionCancellation.Token);
        }
        finally
        {
            executionCancellation.Cancel();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
            {
            }
        }
        result ??= DeploymentExecutionResult.Failed("Deployment execution did not return a result.");
        await context.Entry(ticket).ReloadAsync(token);
        if (ticket.Status != DeploymentQueueTicketStatus.Running ||
            !string.Equals(ticket.ClaimOwner, claimOwner, StringComparison.Ordinal))
            return;

        if (result.Success)
        {
            ticket.Status = DeploymentQueueTicketStatus.Succeeded;
            ticket.Stage = DeploymentStage.Ready;
            ticket.StageMessage = "Runtime operation completed.";
            ticket.ErrorMessage = null;
            ticket.CompletedAt = DateTimeOffset.UtcNow;
            ticket.ClaimOwner = null;
            ticket.ClaimExpiresAt = null;
            ticket.ProtectedPayload = null;
            ticket.ErrorCategory = null;
            ticket.ErrorCode = null;
            ticket.Retryable = false;
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.Runtime.ExecutionSucceeded,
                OperationalEventOutcome.Succeeded,
                "Runtime operation completed successfully."));
            if (RuntimeOperationalEvents.ResourceSucceededCode(ticket) is { } resourceCode)
                events.Append(RuntimeOperationalEvents.Ticket(
                    ticket,
                    resourceCode,
                    OperationalEventOutcome.Succeeded,
                    "Runtime resource operation completed successfully."));
            if (RequiresCapacityReservation(ticket))
                await ConfirmCapacityAsync(context, capacity, ticket, token);
            activity?.SetStatus(ActivityStatusCode.Ok);
            PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "succeeded");
            logger.SystemLog($"Deployment execution completed: ticket={ticket.Id}, node={nodeId}.",
                TaskStatus.Success, LogLevel.Information);
        }
        else
        {
            var error = RuntimeOperationalEvents.Failure(ticket, "runtime.execute");
            await MarkFailedAsync(context, capacity, events, ticket, error, token, result.ErrorMessage);
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "failed");
            logger.SystemLog(
                $"Deployment execution failed: ticket={ticket.Id}, node={nodeId}, error={ticket.ErrorMessage}.",
                TaskStatus.Failed, LogLevel.Warning);
        }

        await context.SaveChangesAsync(token);
        PlatformTelemetry.RecordRuntimeDuration(
            ticket.Kind.ToString(), ticket.Operation.ToString(), Stopwatch.GetElapsedTime(executionStartedAt));
    }

    async Task FailClaimedTicketAsync(Guid ticketId, string claimOwner, Exception exception, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
        var events = scope.ServiceProvider.GetRequiredService<IOperationalEventWriter>();
        var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket is null || ticket.Status != DeploymentQueueTicketStatus.Running ||
            !string.Equals(ticket.ClaimOwner, claimOwner, StringComparison.Ordinal))
            return;
        var error = RuntimeOperationalEvents.Failure(ticket, "runtime.execute", exception);
        await MarkFailedAsync(context, capacity, events, ticket, error, token, exception.Message);
        await context.SaveChangesAsync(token);
        PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "failed");
    }

    async Task RenewClaimLoopAsync(
        Guid ticketId,
        string claimOwner,
        CancellationTokenSource executionCancellation,
        CancellationToken hostToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(executionCancellation.Token))
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            int updated;
            if (context.Database.IsRelational())
            {
                updated = await context.DeploymentQueueTickets
                    .Where(ticket => ticket.Id == ticketId &&
                                     ticket.Status == DeploymentQueueTicketStatus.Running &&
                                     ticket.ClaimOwner == claimOwner)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(ticket => ticket.ClaimExpiresAt,
                            DateTimeOffset.UtcNow.Add(ClaimTimeout)), hostToken);
            }
            else
            {
                var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(
                    item => item.Id == ticketId && item.Status == DeploymentQueueTicketStatus.Running &&
                            item.ClaimOwner == claimOwner, hostToken);
                updated = ticket is null ? 0 : 1;
                if (ticket is not null)
                {
                    ticket.ClaimExpiresAt = DateTimeOffset.UtcNow.Add(ClaimTimeout);
                    await context.SaveChangesAsync(hostToken);
                }
            }

            if (updated != 1)
            {
                executionCancellation.Cancel();
                return;
            }

            var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
            await capacity.RenewAsync(ticketId, hostToken);
        }
    }

    static async Task MarkFailedAsync(
        AppDbContext context,
        FleetCapacityReservationService capacity,
        IOperationalEventWriter events,
        DeploymentQueueTicket ticket,
        OperationalError error,
        CancellationToken token,
        string? message = null)
    {
        ticket.Status = DeploymentQueueTicketStatus.Failed;
        ticket.Stage = DeploymentStage.Failed;
        ticket.ErrorMessage = TrimError(message ?? error.Message);
        ticket.StageMessage = ticket.ErrorMessage;
        ticket.ErrorCategory = error.Category;
        ticket.ErrorCode = error.Code;
        ticket.Retryable = error.Retryable;
        ticket.CompletedAt = DateTimeOffset.UtcNow;
        ticket.ClaimOwner = null;
        ticket.ClaimExpiresAt = null;
        ticket.ProtectedPayload = null;
        events.Append(RuntimeOperationalEvents.Ticket(
            ticket,
            OperationalEventCodes.Runtime.ExecutionFailed,
            OperationalEventOutcome.Failed,
            "Runtime execution failed.",
            OperationalEventSeverity.Error,
            error));
        if (RuntimeOperationalEvents.ResourceFailedCode(ticket) is { } resourceCode)
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                resourceCode,
                OperationalEventOutcome.Failed,
                "Runtime resource operation failed.",
                OperationalEventSeverity.Error,
                error));
        if (RequiresCapacityReservation(ticket))
            await ReleaseCapacityAsync(context, capacity, ticket, token);
    }

    static void AppendExecutionStarted(IOperationalEventWriter events, DeploymentQueueTicket ticket)
    {
        events.Append(RuntimeOperationalEvents.Ticket(
            ticket,
            OperationalEventCodes.Runtime.ExecutionStarted,
            OperationalEventOutcome.Started,
            "Runtime execution claim started."));
        if (RuntimeOperationalEvents.ResourceStartedCode(ticket) is { } resourceCode)
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                resourceCode,
                OperationalEventOutcome.Started,
                "Runtime resource creation started."));
        PlatformTelemetry.RecordRuntimeTransition(ticket.Kind.ToString(), ticket.Stage.ToString(), "started");
    }

    static async Task ConfirmCapacityAsync(AppDbContext context, FleetCapacityReservationService capacity,
        DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.TeamLabRuntimeId is { } runtimeId)
        {
            foreach (var slot in await TeamLabCapacityFacts.LoadAsync(context, runtimeId, token))
                await capacity.ConfirmAsync(ticket.Id, slot.WorkerNodeId, token);
            return;
        }

        if (ticket.TargetNodeId is { } nodeId)
            await capacity.ConfirmAsync(ticket.Id, nodeId, token);
    }

    static async Task ReleaseCapacityAsync(AppDbContext context, FleetCapacityReservationService capacity,
        DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.TeamLabRuntimeId is { } runtimeId)
        {
            foreach (var slot in await TeamLabCapacityFacts.LoadAsync(context, runtimeId, token))
                await capacity.ReleaseAsync(ticket.Id, slot.WorkerNodeId, token);
            return;
        }

        if (ticket.TargetNodeId is { } nodeId)
            await capacity.ReleaseAsync(ticket.Id, nodeId, token);
    }

    static bool RequiresCapacityReservation(DeploymentQueueTicket ticket) =>
        ticket.Operation == RuntimeOperationKind.Create ||
        ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.Operation == RuntimeOperationKind.Reset;

    static string TrimError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Deployment queue execution failed.";
        return message.Length <= 1024 ? message : message[..1024];
    }

    static NodeDispatchCategory ResolveCategory(DeploymentQueueTicket ticket)
    {
        if (ticket.Operation != RuntimeOperationKind.Create)
            return NodeDispatchCategory.Control;
        return ticket.Kind == DeploymentQueueKind.VirtualMachine
            ? NodeDispatchCategory.VmCreate
            : ticket.Kind == DeploymentQueueKind.TeamLabRuntime
                ? NodeDispatchCategory.TeamLabNetwork
                : NodeDispatchCategory.DockerCreate;
    }

    static int ResolveLimit(AgentCapabilityManifest? manifest,
        NodeDispatchCategory category)
    {
        var limits = manifest?.ExecutionLimits;
        return Math.Max(1, category switch
        {
            NodeDispatchCategory.DockerCreate => limits?.DockerCreates ?? 1,
            NodeDispatchCategory.VmCreate => limits?.VmCreates ?? 1,
            NodeDispatchCategory.DockerImageTransfer => limits?.DockerImageTransfers ?? 1,
            NodeDispatchCategory.VmImageTransfer => limits?.VmImageTransfers ?? 1,
            NodeDispatchCategory.TeamLabNetwork => limits?.TeamLabNetworkOperations ?? 1,
            NodeDispatchCategory.Control => limits?.ControlOperations ?? 1,
            _ => 1
        });
    }
}
