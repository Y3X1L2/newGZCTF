using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Modules.Exercise.Domain;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Exercise.Application;

public sealed record ExerciseMutationSubmission(
    Guid ApiTokenId,
    Guid ActorUserId,
    string RouteKey,
    string IdempotencyKey,
    string RequestHash,
    ExerciseMutationJob Job);

public interface IExerciseMutationSubmissionStore
{
    Task<IdempotencyBeginResult> SubmitAsync(
        ExerciseMutationSubmission submission,
        CancellationToken cancellationToken);
}

public sealed record ExerciseCreatePayload(ExerciseCreateModel Model);
public sealed record ExerciseImportPayload(IReadOnlyList<ExerciseImportItemModel> Items);
public sealed record ExerciseDeletePayload(int ExerciseId);

public sealed class ExerciseExternalApplicationService(IExerciseMutationSubmissionStore submissions)
{
    public const string OperationKind = "exercise.mutation.v1";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IdempotencyBeginResult> SubmitCreateAsync(
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        ExerciseCreateModel model,
        string routeKey,
        CancellationToken cancellationToken) =>
        SubmitAsync(
            apiTokenId,
            RequireActor(actor),
            routeKey,
            idempotencyKey,
            ExerciseMutationKind.Create,
            null,
            new ExerciseCreatePayload(model),
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitImportAsync(
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        IReadOnlyList<ExerciseImportItemModel> items,
        string routeKey,
        CancellationToken cancellationToken) =>
        SubmitAsync(
            apiTokenId,
            RequireActor(actor),
            routeKey,
            idempotencyKey,
            ExerciseMutationKind.Import,
            null,
            new ExerciseImportPayload(items),
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitUpdateAsync(
        int exerciseId,
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        ExerciseCreateModel model,
        string routeKey,
        CancellationToken cancellationToken) =>
        SubmitAsync(
            apiTokenId,
            RequireActor(actor),
            routeKey,
            idempotencyKey,
            ExerciseMutationKind.Update,
            exerciseId,
            new ExerciseCreatePayload(model),
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitDeleteAsync(
        int exerciseId,
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        string routeKey,
        CancellationToken cancellationToken) =>
        SubmitAsync(
            apiTokenId,
            RequireActor(actor),
            routeKey,
            idempotencyKey,
            ExerciseMutationKind.Delete,
            exerciseId,
            new ExerciseDeletePayload(exerciseId),
            cancellationToken);

    async Task<IdempotencyBeginResult> SubmitAsync<TPayload>(
        Guid apiTokenId,
        Guid actorUserId,
        string routeKey,
        string idempotencyKey,
        ExerciseMutationKind kind,
        int? exerciseId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        if (exerciseId is <= 0)
            throw new ExerciseApiContractException("exercise_not_found", "The exercise was not found.", 404);

        if (payload is ExerciseCreatePayload create)
            ExerciseWriteValidation.Validate(create.Model);
        if (payload is ExerciseImportPayload import)
        {
            if (import.Items.Count is < 1 or > 100 || import.Items.Any(item => item is null))
                throw new ExerciseApiContractException("exercise_import_invalid", "Import between 1 and 100 exercises.", 422);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in import.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ExternalId) || item.ExternalId.Length > 128 ||
                    !ids.Add(item.ExternalId.Trim()))
                    throw new ExerciseApiContractException("exercise_external_id_invalid", "External IDs must be nonempty and unique within the batch.", 422);
                ExerciseWriteValidation.Validate(item.ToCreateModel());
            }
        }

        var normalizedKey = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { kind, exerciseId, payload }, JsonOptions)));
        return await submissions.SubmitAsync(
            new ExerciseMutationSubmission(
                apiTokenId,
                actorUserId,
                routeKey.Trim(),
                normalizedKey,
                requestHash,
                new ExerciseMutationJob
                {
                    Kind = kind,
                    ExerciseId = exerciseId,
                    PayloadJson = payloadJson
                }),
            cancellationToken);
    }

    static Guid RequireActor(ActorContext actor) => actor.UserId
        ?? throw new ExerciseApiContractException(
            "authentication_required", "Authentication is required.", 401);
}
