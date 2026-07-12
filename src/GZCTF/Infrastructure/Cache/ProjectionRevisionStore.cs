using Microsoft.EntityFrameworkCore;

namespace GZCTF.Infrastructure.Cache;

public interface IProjectionRevisionStore
{
    ValueTask<long> GetAsync(string projection, string resourceKey, CancellationToken cancellationToken = default);
    ValueTask BumpAsync(string projection, string resourceKey, CancellationToken cancellationToken = default);
}

public sealed class ProjectionRevisionStore(AppDbContext context) : IProjectionRevisionStore
{
    public async ValueTask<long> GetAsync(string projection, string resourceKey,
        CancellationToken cancellationToken = default) =>
        await context.ProjectionRevisions.AsNoTracking()
            .Where(item => item.Projection == projection && item.ResourceKey == resourceKey)
            .Select(item => (long?)item.Version)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

    public async ValueTask BumpAsync(string projection, string resourceKey,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.IsNpgsql())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "ProjectionRevisions" ("Projection", "ResourceKey", "Version", "UpdatedAt")
                VALUES ({{projection}}, {{resourceKey}}, 1, CURRENT_TIMESTAMP)
                ON CONFLICT ("Projection", "ResourceKey") DO UPDATE
                SET "Version" = "ProjectionRevisions"."Version" + 1,
                    "UpdatedAt" = CURRENT_TIMESTAMP
                """, cancellationToken);
            return;
        }

        var revision = await context.ProjectionRevisions
            .SingleOrDefaultAsync(item => item.Projection == projection && item.ResourceKey == resourceKey,
                cancellationToken);
        if (revision is null)
        {
            context.ProjectionRevisions.Add(new ProjectionRevision
            {
                Projection = projection,
                ResourceKey = resourceKey,
                Version = 1
            });
        }
        else
        {
            revision.Version++;
            revision.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
