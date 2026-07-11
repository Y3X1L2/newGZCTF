using System.Text.Json;

namespace GZCTF.Modules.Audit.Application;

public interface IApiOperationResultProvider
{
    string Kind { get; }
    Task<JsonElement?> GetResultAsync(Guid operationId, CancellationToken cancellationToken);
}
