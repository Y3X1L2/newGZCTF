using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabRuntimeManagerAuthorizationProvider
{
    Task<bool> CanManageRuntimeAsync(
        int runtimeId,
        Guid actorUserId,
        CancellationToken cancellationToken);
}

public sealed class TeamLabAuthorizationService(
    AppDbContext context,
    IEnumerable<ITeamLabRuntimeManagerAuthorizationProvider> managerProviders)
{
    public async Task RequireRuntimeOwnerAsync(
        Guid runtimeId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var owner = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => item.CreatedById)
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is null)
            throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);
        if (!administrator && owner != actorUserId)
            throw new TeamLabApiContractException("insufficient_permission", "The runtime is not owned by the operation actor.", 403);
    }

    public async Task RequireRuntimeManagerAsync(
        Guid runtimeId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => new { item.Id, item.CreatedById })
            .SingleOrDefaultAsync(cancellationToken);
        if (runtime is null)
            throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);
        if (administrator || runtime.CreatedById == actorUserId) return;

        foreach (var provider in managerProviders)
            if (await provider.CanManageRuntimeAsync(runtime.Id, actorUserId, cancellationToken))
                return;

        throw new TeamLabApiContractException(
            "insufficient_permission",
            "The runtime is not managed by the operation actor.",
            403);
    }
}
