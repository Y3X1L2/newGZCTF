using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Application;

public sealed class PenetrationWorkspaceService(
    AppDbContext context,
    ITeamLabRuntimeApplicationService runtimes)
{
    public async Task<PenetrationWorkspaceModel?> GetAsync(
        int gameId,
        int teamId,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.TeamId == teamId, cancellationToken);
        if (binding is null) return null;
        var runtimePublicId = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.Id == binding.RuntimeId)
            .Select(item => item.PublicId)
            .SingleAsync(cancellationToken);
        var runtime = await runtimes.GetAsync(runtimePublicId, cancellationToken);
        var metadata = await context.PenetrationGameLabBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => new { item.MaxResetCount })
            .SingleAsync(cancellationToken);
        var teamName = await context.Teams.AsNoTracking()
            .Where(item => item.Id == teamId)
            .Select(item => item.Name)
            .SingleAsync(cancellationToken);
        var objectives = await context.PenetrationObjectives.AsNoTracking()
            .Where(item => item.GameId == gameId && item.IsVisible)
            .OrderBy(item => item.OrderIndex)
            .ToArrayAsync(cancellationToken);
        var objectiveIds = objectives.Select(item => item.Id).ToArray();
        var states = await context.PenetrationSubmissions.AsNoTracking()
            .Where(item => item.GameId == gameId && item.TeamId == teamId &&
                           objectiveIds.Contains(item.ObjectiveId))
            .GroupBy(item => item.ObjectiveId)
            .Select(group => new
            {
                ObjectiveId = group.Key,
                Attempts = group.Count(),
                Solved = group.Any(item => item.Status == AnswerResult.Accepted)
            }).ToDictionaryAsync(item => item.ObjectiveId, cancellationToken);
        var resetCount = await context.PenetrationResetRecords.AsNoTracking()
            .CountAsync(item => item.RuntimeId == binding.RuntimeId && !item.ByAdmin &&
                                (item.Status == PenetrationResetStatus.Pending ||
                                 item.Status == PenetrationResetStatus.Running ||
                                 item.Status == PenetrationResetStatus.Succeeded ||
                                 item.Status == PenetrationResetStatus.Failed &&
                                 item.FailureClass == PenetrationResetFailureClass.Scenario),
                cancellationToken);
        return new PenetrationWorkspaceModel(
            gameId, teamId, teamName, runtime.Id, runtime.Status, runtime.Stage, resetCount, metadata.MaxResetCount,
            objectives.Select(item =>
            {
                states.TryGetValue(item.Id, out var state);
                return new PenetrationWorkspaceObjectiveModel(
                    item.Id, item.Key, item.TopologyAssetKey, item.Title, item.Description, item.Category, item.Score,
                    state?.Solved ?? false, state?.Attempts ?? 0, item.MaxAttempts, item.IsCheckpoint,
                    PenetrationObjectiveService.DeserializeKeys(item.PrerequisiteObjectiveKeysJson));
            }).ToArray());
    }
}
