using GZCTF.Models.Data;
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
            await FailClaimedTicketAsync(ticketId, claimOwner, ex.Message, token);
        }

        return true;
    }

    async Task<bool> TryClaimAsync(Guid ticketId, DateTimeOffset now, string claimOwner,
        CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (context.Database.IsRelational())
            return await context.DeploymentQueueTickets
                .Where(ticket => ticket.Id == ticketId && ticket.Status == DeploymentQueueTicketStatus.Scheduled)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(ticket => ticket.Status, DeploymentQueueTicketStatus.Running)
                    .SetProperty(ticket => ticket.ClaimOwner, claimOwner)
                    .SetProperty(ticket => ticket.ClaimExpiresAt, now.Add(ClaimTimeout))
                    .SetProperty(ticket => ticket.StartedAt, now), token) == 1;

        var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket?.Status != DeploymentQueueTicketStatus.Scheduled)
            return false;
        ticket.Status = DeploymentQueueTicketStatus.Running;
        ticket.ClaimOwner = claimOwner;
        ticket.ClaimExpiresAt = now.Add(ClaimTimeout);
        ticket.StartedAt = now;
        await context.SaveChangesAsync(token);
        return true;
    }

    async Task ExecuteClaimedTicketAsync(Guid ticketId, string claimOwner, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
        var executor = scope.ServiceProvider.GetRequiredService<DeploymentExecutionService>();
        var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket is null || ticket.Status != DeploymentQueueTicketStatus.Running ||
            !string.Equals(ticket.ClaimOwner, claimOwner, StringComparison.Ordinal))
            return;

        if (ticket.TargetNodeId is not { } nodeId)
        {
            await MarkFailedAsync(context, capacity, ticket, "Scheduled ticket has no target node.", token);
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
            if (RequiresCapacityReservation(ticket))
                await ConfirmCapacityAsync(context, capacity, ticket, token);
            ticket.Status = DeploymentQueueTicketStatus.Succeeded;
            ticket.Stage = DeploymentStage.Ready;
            ticket.StageMessage = "Runtime operation completed.";
            ticket.ErrorMessage = null;
            ticket.CompletedAt = DateTimeOffset.UtcNow;
            ticket.ClaimOwner = null;
            ticket.ClaimExpiresAt = null;
            ticket.ProtectedPayload = null;
            logger.SystemLog($"Deployment execution completed: ticket={ticket.Id}, node={nodeId}.",
                TaskStatus.Success, LogLevel.Information);
        }
        else
        {
            await MarkFailedAsync(context, capacity, ticket, result.ErrorMessage, token);
            logger.SystemLog(
                $"Deployment execution failed: ticket={ticket.Id}, node={nodeId}, error={ticket.ErrorMessage}.",
                TaskStatus.Failed, LogLevel.Warning);
        }

        await context.SaveChangesAsync(token);
    }

    async Task FailClaimedTicketAsync(Guid ticketId, string claimOwner, string message, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
        var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket is null || ticket.Status != DeploymentQueueTicketStatus.Running ||
            !string.Equals(ticket.ClaimOwner, claimOwner, StringComparison.Ordinal))
            return;
        await MarkFailedAsync(context, capacity, ticket, message, token);
        await context.SaveChangesAsync(token);
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

    static async Task MarkFailedAsync(AppDbContext context, FleetCapacityReservationService capacity,
        DeploymentQueueTicket ticket, string? message, CancellationToken token)
    {
        if (RequiresCapacityReservation(ticket))
            await ReleaseCapacityAsync(context, capacity, ticket, token);
        ticket.Status = DeploymentQueueTicketStatus.Failed;
        ticket.Stage = DeploymentStage.Failed;
        ticket.ErrorMessage = TrimError(message);
        ticket.StageMessage = ticket.ErrorMessage;
        ticket.CompletedAt = DateTimeOffset.UtcNow;
        ticket.ClaimOwner = null;
        ticket.ClaimExpiresAt = null;
        ticket.ProtectedPayload = null;
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
