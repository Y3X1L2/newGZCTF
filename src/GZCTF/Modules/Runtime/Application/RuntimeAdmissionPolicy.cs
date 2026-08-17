using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
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
        var fairnessKey = request.Identity.FairnessKey;
        var count = await context.DeploymentQueueTickets.AsNoTracking()
            .CountAsync(ticket => ticket.Operation == RuntimeOperationKind.Create &&
                                  ticket.Status == DeploymentQueueTicketStatus.Pending &&
                                  ticket.FairnessKey == fairnessKey, token);
        if (count >= Math.Max(1, _options.MaxQueuedCreatesPerOwner))
            throw new RuntimeQueueLimitException("owner_queue_limit_exceeded",
                $"The deployment queue limit for {fairnessKey} has been reached.");
    }
}

public sealed class RuntimeQueueLimitException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class RuntimeApiContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);
