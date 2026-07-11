using GZCTF.Models;
using GZCTF.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class ImageApiTokenResourceGrantPolicy(AppDbContext context) : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "image";

    public Task<bool> CanGrantAsync(
        ActorContext actor,
        string resourceId,
        CancellationToken cancellationToken)
    {
        if (actor.Role >= Role.Admin)
            return Task.FromResult(resourceId == "*" || resourceId.Length > 0);
        if (actor.UserId is not { } actorUserId || resourceId == "*")
            return Task.FromResult(false);
        return context.ImageTemplates.AsNoTracking().AnyAsync(
            template => template.Name == resourceId && template.CreatedById == actorUserId,
            cancellationToken);
    }
}
