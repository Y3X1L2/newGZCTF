using System.Text.Json;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Exercise.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Exercise.Infrastructure;

public sealed class ExerciseMutationResultProvider(AppDbContext context) : IApiOperationResultProvider
{
    public string Kind => ExerciseExternalApplicationService.OperationKind;

    public async Task<JsonElement?> GetResultAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var json = await context.ExerciseMutationJobs.AsNoTracking()
            .Where(job => job.OperationId == operationId)
            .Select(job => job.ResultJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
