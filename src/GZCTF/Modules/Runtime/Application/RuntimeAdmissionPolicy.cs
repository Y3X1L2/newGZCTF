using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Runtime.Application;

public sealed class RuntimeAdmissionPolicy(AppDbContext context, IOptions<RuntimeSchedulingOptions> options)
{
    readonly RuntimeSchedulingOptions _options = options.Value;

    public async Task EnsureQueueCapacityAsync(DeploymentQueueRequest request, CancellationToken token)
    {
        if (request.Operation != RuntimeOperationKind.Create)
            return;
        var ownerKey = RuntimeQueueSelector.OwnerKey(request.OwnerTeamId, request.OwnerUserId);
        var pendingOwners = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.Operation == RuntimeOperationKind.Create &&
                             ticket.Status == DeploymentQueueTicketStatus.Pending)
            .Select(ticket => new { ticket.OwnerTeamId, ticket.OwnerUserId })
            .ToArrayAsync(token);
        var count = pendingOwners.Count(item =>
            RuntimeQueueSelector.OwnerKey(item.OwnerTeamId, item.OwnerUserId) == ownerKey);
        if (count >= Math.Max(1, _options.MaxQueuedCreatesPerOwner))
            throw new RuntimeQueueLimitException("owner_queue_limit_exceeded",
                $"The deployment queue limit for {ownerKey} has been reached.");
    }
}

public sealed class RuntimeQueueLimitException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
