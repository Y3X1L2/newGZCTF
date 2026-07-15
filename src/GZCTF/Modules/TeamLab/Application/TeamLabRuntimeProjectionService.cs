using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeProjectionService(AppDbContext context)
{
    public async Task<TeamLabRuntimeProjectionModel> GetAsync(Guid runtimePublicId, CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);
        var releaseId = runtime.TopologyReleaseId;
        if (releaseId == Guid.Empty)
            throw new TeamLabApiContractException("runtime_invalid", "The TeamLab runtime has no topology release.", 500);
        return new TeamLabRuntimeProjectionModel(
            runtime.PublicId,
            releaseId,
            runtime.Generation,
            runtime.Status,
            Stage(runtime.Status),
            runtime.IsOpenToPlayers,
            runtime.Shards.Where(item => item.Generation == runtime.Generation)
                .OrderBy(item => item.PublicId)
                .Select(shard => new TeamLabRuntimeShardProjectionModel(
                    shard.PublicId,
                    shard.Status,
                    runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
                        .Select(item => item.TopologyKey).Order(StringComparer.Ordinal).ToArray(),
                    runtime.Assets.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
                        .Select(item => item.TopologyKey).Order(StringComparer.Ordinal).ToArray(),
                    shard.LastError)).ToArray(),
            runtime.Networks.Where(item => item.Generation == runtime.Generation)
                .OrderBy(item => item.TopologyKey, StringComparer.Ordinal)
                .Select(item => new TeamLabRuntimeNetworkProjectionModel(
                    item.TopologyKey, item.Name, item.Cidr, item.GatewayIp)).ToArray(),
            runtime.Assets.Where(item => item.Generation == runtime.Generation &&
                                         item.Kind is TeamLabResourceKind.Docker or TeamLabResourceKind.Vm)
                .OrderBy(item => item.TopologyKey, StringComparer.Ordinal)
                .Select(item => new TeamLabRuntimeAssetProjectionModel(
                    item.TopologyKey,
                    item.Name,
                    item.Kind == TeamLabResourceKind.Docker ? TeamLabAssetKind.Docker : TeamLabAssetKind.Vm,
                    item.RuntimeResourceId,
                    item.IpAddress,
                    item.Status,
                    item.LastError)).ToArray(),
            runtime.CreatedAt,
            runtime.UpdatedAt,
            runtime.LastError);
    }

    public async Task<IReadOnlyList<TeamLabRuntimeEventModel>> GetEventsAsync(
        Guid runtimePublicId,
        long after,
        int limit,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimePublicId)
            .Select(item => new { item.Id, item.Generation })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);
        var take = Math.Clamp(limit, 1, 200);
        return await context.TeamLabEvents.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation && item.Id > after)
            .OrderBy(item => item.Id)
            .Take(take)
            .Select(item => new TeamLabRuntimeEventModel(
                item.Id, item.Generation, item.Stage, item.Level, item.Message,
                item.ObjectType, item.ObjectId, item.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<OpenTeamLabRuntimeEventPageModel> GetEventsAsync(
        Guid runtimePublicId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimePublicId)
            .Select(item => new { item.Id, item.Generation })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        var cursor = DecodeEventCursor(after);
        var query = context.TeamLabEvents.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation);
        if (cursor is { } value)
            query = query.Where(item => item.CreatedAt > value.Time ||
                                        item.CreatedAt == value.Time && item.Id > value.Id);
        var rows = await query
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(normalizedLimit + 1)
            .Select(item => new TeamLabRuntimeEventModel(
                item.Id, item.Generation, item.Stage, item.Level, item.Message,
                item.ObjectType, item.ObjectId, item.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(normalizedLimit).ToArray();
        var nextCursor = rows.Length > normalizedLimit
            ? new TimeCursor(page[^1].CreatedAt, page[^1].Cursor).Encode()
            : null;
        return new OpenTeamLabRuntimeEventPageModel(page, nextCursor);
    }

    private static TimeCursor? DecodeEventCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return TimeCursor.Decode(value);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException(
                "runtime_event_cursor_invalid", "The pagination cursor is invalid.", 400);
        }
    }

    private static string Stage(TeamLabRuntimeStatus status) => status switch
    {
        TeamLabRuntimeStatus.Pending => "pending",
        TeamLabRuntimeStatus.Planning => "planning",
        TeamLabRuntimeStatus.Scheduled => "queued",
        TeamLabRuntimeStatus.Deploying => "deploying",
        TeamLabRuntimeStatus.Probing => "probing",
        TeamLabRuntimeStatus.Running => "ready",
        TeamLabRuntimeStatus.Failed => "failed",
        TeamLabRuntimeStatus.CleanupPending => "cleanup-pending",
        TeamLabRuntimeStatus.Destroying => "destroying",
        TeamLabRuntimeStatus.Destroyed => "destroyed",
        TeamLabRuntimeStatus.Stopped => "stopped",
        _ => "unknown"
    };
}
