using GZCTF.Models;
using GZCTF.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Audit.Infrastructure;

public sealed class OperationApiTokenResourceGrantPolicy(AppDbContext context) : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "operation";

    public Task<bool> CanGrantAsync(
        ActorContext actor,
        string resourceId,
        CancellationToken cancellationToken)
    {
        if (actor.Role >= Role.Admin)
            return Task.FromResult(resourceId == "*" || Guid.TryParse(resourceId, out _));
        if (actor.UserId is not { } actorUserId || !Guid.TryParse(resourceId, out var operationId))
            return Task.FromResult(false);
        return context.ApiOperations.AsNoTracking().AnyAsync(
            operation => operation.Id == operationId && operation.ActorUserId == actorUserId,
            cancellationToken);
    }
}
