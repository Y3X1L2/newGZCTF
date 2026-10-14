using GZCTF.Models.Request.Admin;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Modules.Audit.Contracts;

namespace GZCTF.Repositories;

public class LogRepository(AppDbContext context, IOperationalLogDisplayQuery displays) : RepositoryBase(context), ILogRepository
{
    public async Task<LogMessagePageModel> GetLogs(LogQueryModel query, CancellationToken token)
    {
        var take = Math.Clamp(query.Count, 1, 200);
        IQueryable<LogModel> data = Context.Logs.AsNoTracking();

        if (query.Level is not null && query.Level != "All")
            data = data.Where(x => x.Level == query.Level);
        if (query.CorrelationId is { } correlationId)
            data = data.Where(x => x.CorrelationId == correlationId);
        if (!string.IsNullOrWhiteSpace(query.Logger))
            data = data.Where(x => x.Logger == query.Logger);
        if (!string.IsNullOrWhiteSpace(query.EventCode))
            data = data.Where(x => x.EventCode == query.EventCode);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            data = data.Where(x => x.Message.Contains(keyword) ||
                                   x.ResourceDisplayName != null && x.ResourceDisplayName.Contains(keyword) ||
                                   x.EventCode != null && x.EventCode.Contains(keyword));
        }
        if (query.WorkerNodeId is { } workerNodeId)
            data = data.Where(x => x.WorkerNodeId == workerNodeId);
        if (query.DeploymentTicketId is { } deploymentTicketId)
            data = data.Where(x => x.DeploymentTicketId == deploymentTicketId);
        if (!string.IsNullOrWhiteSpace(query.ResourceType))
            data = data.Where(x => x.ResourceType == query.ResourceType);
        if (!string.IsNullOrWhiteSpace(query.ResourceId))
            data = data.Where(x => x.ResourceId == query.ResourceId);
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = TimeCursor.Decode(query.Cursor);
            data = data.Where(item => item.TimeUtc < decoded.Time ||
                                      item.TimeUtc == decoded.Time && item.Id < decoded.Id);
        }

        var rows = await data.OrderByDescending(item => item.TimeUtc).ThenByDescending(item => item.Id)
            .Take(take + 1)
            .ToArrayAsync(token);

        var items = rows.Take(take).Select(LogMessageModel.FromLogModel).ToArray();
        await displays.EnrichAsync(items, token);
        var next = rows.Length > take && items.Length > 0
            ? new TimeCursor(items[^1].Time, items[^1].Id).Encode()
            : null;
        return new LogMessagePageModel(items, next);
    }
}
