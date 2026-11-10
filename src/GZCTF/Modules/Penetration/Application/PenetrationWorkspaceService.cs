using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Application;

public sealed class PenetrationWorkspaceService(
    AppDbContext context,
    NetlabClient netlab, PenetrationTeamLabAdapter scenes)
{
    public async Task<PenetrationWorkspaceModel?> GetAsync(
        int gameId,
        int teamId,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.TeamId == teamId, cancellationToken);
        if (binding?.EnvironmentId is null) return null;
        var state = await netlab.GetAsync<EnvironmentState>($"environments/{Uri.EscapeDataString(binding.EnvironmentId)}/state", cancellationToken);
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
        var resets = await scenes.RefreshResetsAsync(binding.EnvironmentId, cancellationToken);
        var resetCount = resets.Count(x => !x.ByAdmin && x.Status != PenetrationResetStatus.Failed);
        return new PenetrationWorkspaceModel(
            gameId, teamId, teamName, binding.EnvironmentId, PenetrationTeamLabAdapter.Status(state.Status), state.Status, resetCount, metadata.MaxResetCount,
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
