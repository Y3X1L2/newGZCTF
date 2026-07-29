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
