using GZCTF.Models.Request.Admin;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using GZCTF.Infrastructure.Persistence.Queries;

namespace GZCTF.Repositories;

public class LogRepository(AppDbContext context) : RepositoryBase(context), ILogRepository
{
    public async Task<LogMessagePageModel> GetLogs(string? cursor, int count, string? level, CancellationToken token)
    {
        var take = Math.Clamp(count, 1, 200);
        IQueryable<LogModel> data = Context.Logs.AsNoTracking();

        if (level is not null && level != "All")
            data = data.Where(x => x.Level == level);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var decoded = TimeCursor.Decode(cursor);
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
                Status = item.Status
            })
            .ToArrayAsync(token);

        var items = rows.Take(take).ToArray();
        var next = rows.Length > take && items.Length > 0
            ? new TimeCursor(items[^1].Time, items[^1].Id).Encode()
            : null;
        return new LogMessagePageModel(items, next);
    }
}
