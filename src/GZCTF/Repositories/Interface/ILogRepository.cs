using GZCTF.Models.Request.Admin;

namespace GZCTF.Repositories.Interface;

public interface ILogRepository : IRepository
{
    /// <summary>
    /// Get logs with pagination and optional level filtering
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="count"></param>
    /// <param name="level"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<LogMessagePageModel> GetLogs(string? cursor, int count, string? level, CancellationToken token);
}
