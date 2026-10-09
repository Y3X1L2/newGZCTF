using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Content.Application;

public interface IImageSourceMigrationStore
{
    Task<IdempotencyBeginResult> SubmitAsync(int templateId, Guid actorId, string idempotencyKey, CancellationToken token);
    Task<ApiOperation?> FindAsync(int templateId, Guid operationId, CancellationToken token);
}

public sealed class ImageSourceMigrationService(IImageSourceMigrationStore store)
{
    public const string OperationKind = "image.source-migrate";

    public Task<IdempotencyBeginResult> SubmitAsync(int templateId, ActorContext actor, string idempotencyKey,
        CancellationToken token)
    {
        RequireAdministrator(actor);
        if (templateId <= 0) throw new ImageSourceMigrationContractException("image_not_found", "Image template was not found.", 404);
        _ = ExternalIdempotencyKey.Normalize(idempotencyKey);
        return store.SubmitAsync(templateId, actor.UserId!.Value, idempotencyKey, token);
    }

    public Task<ApiOperation?> FindAsync(int templateId, Guid operationId, ActorContext actor, CancellationToken token)
    {
        RequireAdministrator(actor);
        return store.FindAsync(templateId, operationId, token);
    }

    static void RequireAdministrator(ActorContext actor)
    {
        if (actor.Role < Role.Admin || !actor.UserId.HasValue)
            throw new ImageSourceMigrationContractException("source_migration_forbidden", "Source migration requires an administrator.", 403);
    }
}

public sealed class ImageSourceMigrationContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);
