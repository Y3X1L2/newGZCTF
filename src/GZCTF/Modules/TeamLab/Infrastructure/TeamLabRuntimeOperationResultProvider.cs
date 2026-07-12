using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabRuntimeOperationResultProvider(AppDbContext context) : IApiOperationResultProvider
{
    public string Kind => TeamLabRuntimeOperationApplicationService.OperationKind;

    public async Task<JsonElement?> GetResultAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var json = await context.TeamLabRuntimeOperationJobs.AsNoTracking()
            .Where(item => item.OperationId == operationId)
            .Select(item => item.ResultJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
