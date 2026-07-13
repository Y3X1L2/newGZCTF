using GZCTF.Models.Request.Admin;

namespace GZCTF.Repositories.Interface;

public interface ILogRepository : IRepository
{
    /// <summary>
    /// Get logs with pagination and optional level filtering
    /// </summary>
    /// <param name="query"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<LogMessagePageModel> GetLogs(LogQueryModel query, CancellationToken token);
}
