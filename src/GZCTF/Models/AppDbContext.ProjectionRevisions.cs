using GZCTF.Infrastructure.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GZCTF.Models;

public partial class AppDbContext
{
    internal bool SuppressProjectionRevisionBumps { get; set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        if (SuppressProjectionRevisionBumps || !Database.IsNpgsql())
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        var targets = CollectProjectionRevisionTargets();
        if (targets.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        IDbContextTransaction? ownedTransaction = null;
        if (Database.CurrentTransaction is null)
            ownedTransaction = await Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            foreach (var (projection, resourceKey) in targets.OrderBy(item => item.Projection)
                         .ThenBy(item => item.ResourceKey))
                await BumpProjectionRevisionAsync(projection, resourceKey, cancellationToken);

            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (ownedTransaction is not null)
                await ownedTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }

    private HashSet<(string Projection, string ResourceKey)> CollectProjectionRevisionTargets()
    {
        var targets = new HashSet<(string, string)>();
        foreach (var entry in ChangeTracker.Entries().Where(entry =>
                     entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            switch (entry.Entity)
            {
                case Game game when game.Id > 0:
                    Scoreboard(game.Id);
                    targets.Add((CachePolicyCatalog.TheoryStatistics.Name, game.Id.ToString()));
                    break;
                case Submission submission:
                    Scoreboard(submission.GameId);
                    break;
                case Participation participation:
                    Scoreboard(participation.GameId);
                    break;
                case PenetrationSubmission penetration:
                    Scoreboard(penetration.GameId);
                    break;
                case AwdpService service:
                    Scoreboard(service.GameId);
                    break;
                case AwdpRound round:
                    Scoreboard(round.GameId);
                    break;
                case TheoryPaper paper:
                    targets.Add((CachePolicyCatalog.TheoryStatistics.Name, paper.GameId.ToString()));
                    break;
                case TheoryAnswerSheet sheet:
                    targets.Add((CachePolicyCatalog.TheoryStatistics.Name, sheet.GameId.ToString()));
                    break;
                case TrainingCourse:
                case TrainingCourseTeacher:
                case TrainingCourseEnrollment:
                case TrainingCourseChapter:
                case TrainingCourseResource:
                case TrainingCourseChallenge:
                case TrainingCourseChapterChallenge:
                case TrainingCourseTheoryQuestion:
                case TrainingCourseChapterTheoryPaper:
                case TrainingCourseChapterTheoryQuestion:
                    targets.Add((CachePolicyCatalog.TrainingStatistics.Name, "__global__"));
                    break;
                case TrainingCourseProgress progress:
                    TrainingUser(progress.UserId);
                    break;
                case TrainingChapterProgress progress:
                    TrainingUser(progress.UserId);
                    break;
                case TrainingCourseSubmission submission:
                    TrainingUser(submission.UserId);
                    break;
                case TrainingCheckIn checkIn:
                    TrainingUser(checkIn.UserId);
                    break;
                case TrainingCourseChapterTheorySheet sheet:
                    TrainingUser(sheet.UserId);
                    break;
            }
        }

        return targets;

        void Scoreboard(int gameId)
        {
            if (gameId > 0)
                targets.Add((CachePolicyCatalog.Scoreboard.Name, gameId.ToString()));
        }

        void TrainingUser(Guid userId)
        {
            if (userId != Guid.Empty)
                targets.Add((CachePolicyCatalog.TrainingStatistics.Name, userId.ToString("N")));
        }
    }

    private Task<int> BumpProjectionRevisionAsync(string projection, string resourceKey,
        CancellationToken cancellationToken) =>
        Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "ProjectionRevisions" ("Projection", "ResourceKey", "Version", "UpdatedAt")
            VALUES ({{projection}}, {{resourceKey}}, 1, CURRENT_TIMESTAMP)
            ON CONFLICT ("Projection", "ResourceKey") DO UPDATE
            SET "Version" = "ProjectionRevisions"."Version" + 1,
                "UpdatedAt" = CURRENT_TIMESTAMP
            """, cancellationToken);
}
