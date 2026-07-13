using GZCTF.Models.Request.Admin;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using GZCTF.Infrastructure.Persistence.Queries;

namespace GZCTF.Repositories;

public class LogRepository(AppDbContext context) : RepositoryBase(context), ILogRepository
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
            .Select(item => new LogMessageModel
            {
                Id = item.Id,
                Time = item.TimeUtc,
                Level = item.Level,
                UserName = item.UserName,
                IP = item.RemoteIP,
                Msg = item.Message,
                Status = item.Status,
                CorrelationId = item.CorrelationId,
                TraceId = item.TraceId,
                EventCode = item.EventCode,
                ErrorCategory = item.ErrorCategory,
                ErrorCode = item.ErrorCode,
                WorkerNodeId = item.WorkerNodeId,
                WorkerNodeName = item.WorkerNodeName,
                DeploymentTicketId = item.DeploymentTicketId,
                ResourceType = item.ResourceType,
                ResourceId = item.ResourceId,
                ResourceDisplayName = item.ResourceDisplayName
            })
            .ToArrayAsync(token);

        var items = rows.Take(take).ToArray();
        var next = rows.Length > take && items.Length > 0
            ? new TimeCursor(items[^1].Time, items[^1].Id).Encode()
            : null;
        return new LogMessagePageModel(items, next);
    }
}
