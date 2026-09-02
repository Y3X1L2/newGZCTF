using System.Text.Json;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Exercise.Application;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Modules.Exercise.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Exercise.Infrastructure;

public sealed class ExerciseMutationOperationHandler(
    AppDbContext context,
    IExerciseManagementService managementService) : IApiOperationHandler
{
    public string Kind => ExerciseExternalApplicationService.OperationKind;

    public async Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        var job = await context.ExerciseMutationJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken)
            ?? throw new ApiOperationTerminalException(
                "exercise_job_not_found", "The persisted exercise operation payload was not found.");
        if (job.ResultJson is not null)
            return;
        if (string.IsNullOrWhiteSpace(job.PayloadJson))
            throw new ApiOperationTerminalException(
                "exercise_payload_missing", "The persisted exercise operation payload is unavailable.");

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            job.ResultJson = job.Kind switch
            {
                ExerciseMutationKind.Create => await CreateAsync(job, cancellationToken),
                ExerciseMutationKind.Import => await ImportAsync(job, cancellationToken),
                ExerciseMutationKind.Update => await UpdateAsync(job, cancellationToken),
                ExerciseMutationKind.Delete => await DeleteAsync(job, cancellationToken),
                _ => throw new ApiOperationTerminalException(
                    "exercise_operation_invalid", "The exercise operation kind is invalid.")
            };
            job.PayloadJson = null;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (JsonException)
        {
            throw new ApiOperationTerminalException(
                "exercise_payload_invalid", "The persisted exercise operation payload is invalid.");
        }
    }

    public async Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var job = await context.ExerciseMutationJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken);
        if (job is null)
            return;
        job.PayloadJson = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    async Task<string> CreateAsync(ExerciseMutationJob job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ExerciseCreatePayload>(
            job.PayloadJson!, ExerciseExternalApplicationService.JsonOptions) ?? throw new JsonException();
        var exercise = await CreateExerciseAsync(payload.Model, cancellationToken);
        exercise.CreatedById = await GetActorUserIdAsync(job.OperationId, cancellationToken);
        await managementService.CreateExerciseAsync(exercise, cancellationToken);
        job.ExerciseId = exercise.Id;
        return SerializeResult([new ExerciseImportResultItem
        {
            ExternalId = string.Empty,
            ExerciseId = exercise.Id,
            Title = exercise.Title
        }], [], []);
    }

    async Task<string> ImportAsync(ExerciseMutationJob job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ExerciseImportPayload>(
            job.PayloadJson!, ExerciseExternalApplicationService.JsonOptions) ?? throw new JsonException();
        var imported = new List<ExerciseImportResultItem>();
        foreach (var item in payload.Items)
        {
            var exercise = await CreateExerciseAsync(item, cancellationToken);
            exercise.CreatedById = await GetActorUserIdAsync(job.OperationId, cancellationToken);
            await managementService.CreateExerciseAsync(exercise, cancellationToken);
            imported.Add(new ExerciseImportResultItem
            {
                ExternalId = item.ExternalId,
                ExerciseId = exercise.Id,
                Title = exercise.Title
            });
        }

        return SerializeResult(imported, [], []);
    }

    async Task<string> UpdateAsync(ExerciseMutationJob job, CancellationToken cancellationToken)
    {
        var exerciseId = job.ExerciseId
            ?? throw new ApiOperationTerminalException("exercise_not_found", "The exercise was not found.");
        var payload = JsonSerializer.Deserialize<ExerciseCreatePayload>(
            job.PayloadJson!, ExerciseExternalApplicationService.JsonOptions) ?? throw new JsonException();
        var existing = await managementService.GetExerciseForUpdateAsync(exerciseId, cancellationToken)
            ?? throw new ApiOperationTerminalException("exercise_not_found", "The exercise was not found.");
        ApplyScalars(existing, payload.Model);
        await managementService.UpdateExerciseWithRelationsAsync(
            existing, payload.Model.Flags, payload.Model.Attachment, cancellationToken);
        return SerializeResult([], [exerciseId], []);
    }

    async Task<string> DeleteAsync(ExerciseMutationJob job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ExerciseDeletePayload>(
            job.PayloadJson!, ExerciseExternalApplicationService.JsonOptions) ?? throw new JsonException();
        if (await managementService.GetExerciseForUpdateAsync(payload.ExerciseId, cancellationToken) is null)
            throw new ApiOperationTerminalException("exercise_not_found", "The exercise was not found.");
        await managementService.RemoveExerciseAsync(payload.ExerciseId, cancellationToken);
        job.ExerciseId = null;
        return SerializeResult([], [], [payload.ExerciseId]);
    }

    static string SerializeResult(
        IReadOnlyList<ExerciseImportResultItem> imported,
        IReadOnlyList<int> updated,
        IReadOnlyList<int> deleted) =>
        JsonSerializer.Serialize(
            new ExerciseMutationResult(imported, updated, deleted),
            ExerciseExternalApplicationService.JsonOptions);

    Task<Guid?> GetActorUserIdAsync(Guid operationId, CancellationToken cancellationToken) =>
        context.ApiOperations.AsNoTracking()
            .Where(operation => operation.Id == operationId)
            .Select(operation => operation.ActorUserId)
            .SingleOrDefaultAsync(cancellationToken);

    async Task<ExerciseChallenge> CreateExerciseAsync(ExerciseCreateModel model, CancellationToken cancellationToken)
    {
        var exercise = new ExerciseChallenge();
        ApplyScalars(exercise, model);
        await ApplyRelationsAsync(exercise, model.Flags, model.Attachment, cancellationToken);
        return exercise;
    }

    async Task<ExerciseChallenge> CreateExerciseAsync(ExerciseImportItemModel model, CancellationToken cancellationToken)
    {
        var exercise = new ExerciseChallenge
        {
            Title = model.Title,
            Content = model.Content,
            Category = model.Category,
            Type = model.Type,
            Difficulty = model.Difficulty,
            Credit = model.Credit,
            Tags = model.Tags ?? [],
            Hints = model.Hints,
            IsEnabled = model.IsEnabled
        };
        await ApplyRelationsAsync(exercise, model.Flags, model.Attachment, cancellationToken);
        return exercise;
    }

    static void ApplyScalars(ExerciseChallenge exercise, ExerciseCreateModel model)
    {
        exercise.Title = model.Title;
        exercise.Content = model.Content;
        exercise.Category = model.Category;
        exercise.Type = model.Type;
        exercise.Difficulty = model.Difficulty;
        exercise.Credit = model.Credit;
        exercise.Tags = model.Tags ?? [];
        exercise.Hints = model.Hints;
        exercise.ContainerImage = model.ContainerImage;
        exercise.MemoryLimit = model.MemoryLimit;
        exercise.StorageLimit = model.StorageLimit;
        exercise.CPUCount = model.CPUCount;
        exercise.ExposePort = model.ExposePort;
        exercise.NetworkMode = model.NetworkMode;
        exercise.FlagTemplate = model.FlagTemplate;
        exercise.Environment = model.Environment;
        exercise.ImageTemplateId = model.ImageTemplateId;
        exercise.IsEnabled = model.IsEnabled;
        exercise.TrainingCourseId = null;
    }

    async Task ApplyRelationsAsync(
        ExerciseChallenge exercise,
        List<ExerciseOpenApiFlagModel>? flags,
        ExerciseOpenApiAttachmentModel? attachment,
        CancellationToken cancellationToken)
    {
        exercise.Flags = [];
        foreach (var flag in flags ?? [])
        {
            exercise.Flags.Add(new FlagContext
            {
                Exercise = exercise,
                Flag = flag.Flag,
                IsOccupied = false,
                OrderIndex = flag.OrderIndex,
                Description = flag.Description,
                ScoreMode = flag.ScoreMode,
                FixedScore = flag.FixedScore,
                MaxAttempts = flag.MaxAttempts,
                AttachmentHash = flag.AttachmentHash,
                AnswerType = flag.AnswerType,
                CustomName = flag.CustomName,
                Attachment = await CreateAttachmentAsync(flag.Attachment, cancellationToken)
            });
        }
        exercise.Attachment = await CreateAttachmentAsync(attachment, cancellationToken);
    }

    async Task<Attachment?> CreateAttachmentAsync(ExerciseOpenApiAttachmentModel? model,
        CancellationToken cancellationToken)
    {
        if (model is null) return null;
        if (!string.IsNullOrWhiteSpace(model.FileHash))
        {
            var file = await context.Files.SingleOrDefaultAsync(item => item.Hash == model.FileHash, cancellationToken)
                ?? throw new ApiOperationTerminalException("asset_not_found", "The attachment asset was not found.");
            return new Attachment { Type = FileType.Local, LocalFile = file };
        }
        if (!string.IsNullOrWhiteSpace(model.RemoteUrl))
            return new Attachment { Type = FileType.Remote, RemoteUrl = model.RemoteUrl };
        throw new ApiOperationTerminalException("attachment_invalid", "An attachment requires fileHash or remoteUrl.");
    }
}
