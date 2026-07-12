using GZCTF.Infrastructure.Cache;
using GZCTF.Models.Request.Game;
using GZCTF.Services;

namespace GZCTF.Modules.Theory.Application;

public sealed class TheoryStatisticsProjectionService(
    TheoryExamService theory,
    IPlatformCache cache)
{
    public ValueTask<TheoryScoreboardItemModel[]> GetScoreboardAsync(int gameId,
        CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync(CachePolicyCatalog.TheoryStatistics, gameId.ToString(), async token =>
        {
            var results = await theory.BuildResults(gameId, token);
            return results.Scoreboard.ToArray();
        }, cancellationToken);

    public ValueTask InvalidateAsync(int gameId, CancellationToken cancellationToken = default) =>
        cache.InvalidateAsync(CachePolicyCatalog.TheoryStatistics, gameId.ToString(), cancellationToken);
}
