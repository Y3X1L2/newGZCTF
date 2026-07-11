using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Ctf.Application;
using GZCTF.Modules.Ctf.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Ctf.Infrastructure;

public sealed class ChallengeMutationResultProvider(AppDbContext context) : IApiOperationResultProvider
{
    public string Kind => ChallengeExternalApplicationService.OperationKind;

    public async Task<JsonElement?> GetResultAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var json = await context.Set<ChallengeMutationJob>().AsNoTracking()
            .Where(job => job.OperationId == operationId)
            .Select(job => job.ResultJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
