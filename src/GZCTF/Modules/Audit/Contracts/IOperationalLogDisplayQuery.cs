using GZCTF.Models.Request.Admin;

namespace GZCTF.Modules.Audit.Contracts;

public interface IOperationalLogDisplayQuery
{
    Task EnrichAsync(IReadOnlyList<LogMessageModel> rows, CancellationToken token);
}
