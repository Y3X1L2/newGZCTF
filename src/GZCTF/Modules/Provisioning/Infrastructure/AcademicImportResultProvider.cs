using System.Text.Json;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Provisioning.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Provisioning.Infrastructure;

public sealed class AcademicImportResultProvider(AppDbContext context) : IApiOperationResultProvider
{
    public string Kind => AcademicImportApplicationService.OperationKind;

    public async Task<JsonElement?> GetResultAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var json = await context.AcademicImportJobs.AsNoTracking()
            .Where(job => job.OperationId == operationId)
            .Select(job => job.ResultJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
