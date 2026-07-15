using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabRuntimeOperationResultProvider(
    AppDbContext context,
    TeamLabAccessGrantService access) : IApiOperationResultProvider
{
    public string Kind => TeamLabRuntimeOperationApplicationService.OperationKind;

    public async Task<JsonElement?> GetResultAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var job = await context.TeamLabRuntimeOperationJobs.AsNoTracking()
            .Where(item => item.OperationId == operationId)
            .Select(item => new { item.Kind, item.ResultJson })
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null || string.IsNullOrWhiteSpace(job.ResultJson)) return null;
        if (job.Kind == TeamLabRuntimeOperationKind.AccessGrantCreate)
        {
            var result = await access.GetOperationResultAsync(operationId, cancellationToken);
            return result is null ? null : JsonSerializer.SerializeToElement(result);
        }
        using var document = JsonDocument.Parse(job.ResultJson);
        return document.RootElement.Clone();
    }
}
