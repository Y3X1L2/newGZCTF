using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Repositories.Interface;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Exercise;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Exercise.Application;

public sealed class ExerciseManagementService(
    AppDbContext context,
    IExerciseChallengeRepository exerciseRepository,
    IBlobRepository blobRepository) : IExerciseManagementService
{
    public async Task<ExerciseChallenge> CreateExerciseAsync(ExerciseChallenge exercise, CancellationToken token = default) =>
        await exerciseRepository.CreateExercise(exercise, token);

    public async Task<ExerciseChallenge> CreateExerciseWithRelationsAsync(
        ExerciseChallenge exercise,
        List<ExerciseFlagCreateModel>? flags,
        AttachmentCreateModel? attachment,
        CancellationToken token = default)
    {
        exercise.Attachment = await CreateAttachmentAsync(attachment, token);
        exercise.Flags = await CreateFlagsAsync(exercise, flags, token);
        return await exerciseRepository.CreateExercise(exercise, token);
    }

    public async Task<ExerciseChallenge> UpdateExerciseAsync(ExerciseChallenge exercise, CancellationToken token = default)
    {
        var existing = await context.ExerciseChallenges
            .FirstOrDefaultAsync(item => item.Id == exercise.Id && item.TrainingCourseId == null, token)
            ?? throw new InvalidOperationException($"Public exercise {exercise.Id} not found");
        context.Entry(existing).CurrentValues.SetValues(exercise);
        existing.TrainingCourseId = null;
        await context.SaveChangesAsync(token);
        return existing;
    }

    public async Task<ExerciseChallenge?> GetExerciseForUpdateAsync(int exerciseId, CancellationToken token = default) =>
        await context.ExerciseChallenges
            .Include(e => e.Flags)
            .Include(e => e.Attachment)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.TrainingCourseId == null, token);

    public async Task<ExerciseChallenge> UpdateExerciseWithRelationsAsync(
        ExerciseChallenge exercise,
        List<ExerciseOpenApiFlagModel>? flags,
        ExerciseOpenApiAttachmentModel? attachment,
        CancellationToken token = default)
    {
        var existing = await context.ExerciseChallenges
            .Include(e => e.Flags)
            .Include(e => e.Attachment)
            .FirstOrDefaultAsync(e => e.Id == exercise.Id && e.TrainingCourseId == null, token)
            ?? throw new InvalidOperationException($"Public exercise {exercise.Id} not found");

        context.Entry(existing).CurrentValues.SetValues(exercise);
        existing.TrainingCourseId = null;

        if (flags is not null)
        {
            foreach (var existingFlag in existing.Flags)
                await blobRepository.DeleteAttachment(existingFlag.Attachment, token);
            context.FlagContexts.RemoveRange(existing.Flags);
            foreach (var flag in flags)
            {
                var flagCtx = new FlagContext
                {
                    Flag = flag.Flag,
                    OrderIndex = flag.OrderIndex,
                    Description = flag.Description,
                    ScoreMode = flag.ScoreMode,
                    FixedScore = flag.FixedScore,
                    MaxAttempts = flag.MaxAttempts,
                    AttachmentHash = flag.AttachmentHash,
                    AnswerType = flag.AnswerType,
                    CustomName = flag.CustomName,
                    Exercise = existing,
                    ExerciseId = exercise.Id
                };
                if (flag.Attachment is not null)
                    flagCtx.Attachment = CreateAttachment(flag.Attachment);
                context.FlagContexts.Add(flagCtx);
            }
        }

        if (attachment is not null)
        {
            await blobRepository.DeleteAttachment(existing.Attachment, token);
            existing.Attachment = CreateAttachment(attachment);
        }

        await context.SaveChangesAsync(token);
        return existing;
    }

    static Attachment? CreateAttachment(ExerciseOpenApiAttachmentModel? model) => model is null
        ? null
        : new Attachment { Type = FileType.Remote, RemoteUrl = model.RemoteUrl };

    public async Task<ExerciseChallenge> UpdateExerciseWithRelationsAsync(
        ExerciseChallenge exercise,
        List<ExerciseFlagCreateModel>? flags,
        AttachmentCreateModel? attachment,
        CancellationToken token = default)
    {
        var existing = await context.ExerciseChallenges
            .Include(item => item.Flags)
            .Include(item => item.Attachment)
            .FirstOrDefaultAsync(item => item.Id == exercise.Id && item.TrainingCourseId == null, token)
            ?? throw new InvalidOperationException($"Public exercise {exercise.Id} not found");

        context.Entry(existing).CurrentValues.SetValues(exercise);
        existing.TrainingCourseId = null;
        var requestedFlags = flags ?? [];
        var requestedIds = requestedFlags.Select(flag => flag.Id).OfType<int>().ToHashSet();
        foreach (var removedFlag in existing.Flags.Where(flag => !requestedIds.Contains(flag.Id)).ToArray())
        {
            await blobRepository.DeleteAttachment(removedFlag.Attachment, token);
            context.FlagContexts.Remove(removedFlag);
        }

        foreach (var model in requestedFlags)
        {
            var flag = model.Id.HasValue
                ? existing.Flags.FirstOrDefault(item => item.Id == model.Id.Value)
                : null;
            if (flag is null)
            {
                flag = await CreateFlagAsync(existing, model, token);
                context.FlagContexts.Add(flag);
            }
            else
            {
                await UpdateFlagAsync(flag, model, token);
            }
        }

        if (!AttachmentMatches(existing.Attachment, attachment))
        {
            var replacement = await CreateAttachmentAsync(attachment, token);
            await blobRepository.DeleteAttachment(existing.Attachment, token);
            existing.Attachment = replacement;
        }
        await context.SaveChangesAsync(token);
        return existing;
    }

    async Task<List<FlagContext>> CreateFlagsAsync(
        ExerciseChallenge exercise,
        List<ExerciseFlagCreateModel>? flags,
        CancellationToken token)
    {
        var result = new List<FlagContext>();
        foreach (var flag in flags ?? [])
            result.Add(await CreateFlagAsync(exercise, flag, token));

        return result;
    }

    async Task<FlagContext> CreateFlagAsync(
        ExerciseChallenge exercise,
        ExerciseFlagCreateModel model,
        CancellationToken token) => new()
    {
        Exercise = exercise,
        Flag = model.Flag,
        IsOccupied = false,
        OrderIndex = model.OrderIndex,
        Description = model.Description,
        ScoreMode = model.ScoreMode,
        FixedScore = model.FixedScore,
        MaxAttempts = model.MaxAttempts,
        AttachmentHash = model.AttachmentHash,
        AnswerType = model.AnswerType,
        CustomName = model.CustomName,
        Attachment = model.ToAttachment(await blobRepository.GetBlobByHash(model.FileHash, token))
    };

    async Task UpdateFlagAsync(
        FlagContext flag,
        ExerciseFlagCreateModel model,
        CancellationToken token)
    {
        flag.Flag = model.Flag;
        flag.IsOccupied = false;
        flag.OrderIndex = model.OrderIndex;
        flag.Description = model.Description;
        flag.ScoreMode = model.ScoreMode;
        flag.FixedScore = model.FixedScore;
        flag.MaxAttempts = model.MaxAttempts;
        flag.AttachmentHash = model.AttachmentHash;
        flag.AnswerType = model.AnswerType;
        flag.CustomName = model.CustomName;
        if (AttachmentMatches(flag.Attachment, model.AttachmentType, model.FileHash, model.RemoteUrl))
            return;
        var replacement = model.ToAttachment(await blobRepository.GetBlobByHash(model.FileHash, token));
        await blobRepository.DeleteAttachment(flag.Attachment, token);
        flag.Attachment = replacement;
    }

    async Task<Attachment?> CreateAttachmentAsync(AttachmentCreateModel? model, CancellationToken token) => model is null
        ? null
        : model.ToAttachment(await blobRepository.GetBlobByHash(model.FileHash, token));

    static bool AttachmentMatches(Attachment? attachment, AttachmentCreateModel? model) => model is null
        ? attachment is null
        : AttachmentMatches(attachment, model.AttachmentType, model.FileHash, model.RemoteUrl);

    static bool AttachmentMatches(
        Attachment? attachment,
        FileType type,
        string? fileHash,
        string? remoteUrl) => type switch
    {
        FileType.None => attachment is null,
        FileType.Local => attachment?.Type == FileType.Local &&
                          string.Equals(attachment.LocalFile?.Hash, fileHash, StringComparison.OrdinalIgnoreCase),
        FileType.Remote => attachment?.Type == FileType.Remote &&
                           string.Equals(attachment.RemoteUrl, remoteUrl, StringComparison.Ordinal),
        _ => false
    };

    public async Task RemoveExerciseAsync(int exerciseId, CancellationToken token = default)
    {
        var exercise = await context.ExerciseChallenges
            .Include(e => e.Attachment)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.TrainingCourseId == null, token);

        if (exercise is not null)
            await exerciseRepository.RemoveExercise(exercise, token);
    }

    public async Task<ExerciseChallenge> ImportFromGameChallengeAsync(int gameChallengeId, CancellationToken token = default)
    {
        var gameChallenge = await context.GameChallenges
            .AsNoTracking()
            .Include(gc => gc.Attachment)
            .ThenInclude(a => a!.LocalFile)
            .Include(gc => gc.Flags)
            .ThenInclude(flag => flag.Attachment)
            .ThenInclude(attachment => attachment!.LocalFile)
            .FirstOrDefaultAsync(gc => gc.Id == gameChallengeId, token)
            ?? throw new InvalidOperationException($"Game challenge {gameChallengeId} not found");

        if (!CanCollect(gameChallenge))
            throw new InvalidOperationException("Only source challenges with a runnable image and a flag can enter the exercise pool.");

        var existing = await context.ExerciseChallenges
            .FirstOrDefaultAsync(e => e.PoolSource == ExercisePoolSource.Game &&
                                     e.SourceChallengeId == gameChallenge.Id &&
                                     e.TrainingCourseId == null, token);

        if (existing is not null)
            return existing;

        await using var transaction = await context.Database.BeginTransactionAsync(token);

        var exercise = new ExerciseChallenge
        {
            Title = gameChallenge.Title,
            Content = gameChallenge.Content,
            Category = gameChallenge.Category,
            Type = gameChallenge.Type,
            Hints = gameChallenge.Hints,
            IsEnabled = true,
            ContainerImage = gameChallenge.ContainerImage,
            MemoryLimit = gameChallenge.MemoryLimit,
            StorageLimit = gameChallenge.StorageLimit,
            CPUCount = gameChallenge.CPUCount,
            ExposePort = gameChallenge.ExposePort,
            NetworkMode = gameChallenge.NetworkMode,
            FileName = gameChallenge.FileName,
            FlagTemplate = gameChallenge.FlagTemplate,
            SubmissionLimit = gameChallenge.SubmissionLimit,
            Environment = gameChallenge.Environment,
            ImageTemplateId = gameChallenge.ImageTemplateId,
            Difficulty = Difficulty.Normal,
            Tags = [gameChallenge.Category.ToString()],
            Credit = false,
            PoolSource = ExercisePoolSource.Game,
            SourceGameId = gameChallenge.GameId,
            SourceChallengeId = gameChallenge.Id,
            MinimumVisibleRole = Role.Teacher,
            Attachment = await CloneAttachmentAsync(gameChallenge.Attachment, token)
        };

        foreach (var flag in gameChallenge.Flags)
        {
            exercise.Flags.Add(new FlagContext
            {
                Flag = flag.Flag,
                Exercise = exercise,
                IsOccupied = false,
                OrderIndex = flag.OrderIndex,
                Description = flag.Description,
                ScoreMode = flag.ScoreMode,
                FixedScore = flag.FixedScore,
                MaxAttempts = flag.MaxAttempts,
                AttachmentHash = flag.AttachmentHash,
                AnswerType = flag.AnswerType,
                CustomName = flag.CustomName,
                Attachment = await CloneAttachmentAsync(flag.Attachment, token)
            });
        }

        await exerciseRepository.CreateExercise(exercise, token);
        await transaction.CommitAsync(token);
        return exercise;
    }

    async Task<Attachment?> CloneAttachmentAsync(Attachment? source, CancellationToken token)
    {
        if (source is null || source.Type == FileType.None)
            return null;

        if (source.Type == FileType.Remote)
            return new Attachment { Type = FileType.Remote, RemoteUrl = source.RemoteUrl };

        if (source.LocalFile is null)
            throw new InvalidOperationException($"Local attachment {source.Id} has no blob.");

        var localFile = await blobRepository.IncrementBlobReference(source.LocalFile.Hash, token)
            ?? throw new InvalidOperationException($"Attachment blob {source.LocalFile.Hash} not found.");
        return new Attachment
        {
            Type = FileType.Local,
            LocalFileId = localFile.Id,
            LocalFile = localFile
        };
    }

    public async Task<ExerciseChallenge[]> ImportFromGameAsync(int gameId, int[]? challengeIds = null, CancellationToken token = default)
    {
        var query = context.GameChallenges
            .Where(gc => gc.GameId == gameId);

        if (challengeIds is { Length: > 0 })
            query = query.Where(gc => challengeIds.Contains(gc.Id));

        var challenges = await query
            .Include(challenge => challenge.Flags)
            .ToArrayAsync(token);
        var results = new List<ExerciseChallenge>();

        foreach (var challenge in challenges)
        {
            if (!CanCollect(challenge))
                continue;
            var imported = await ImportFromGameChallengeAsync(challenge.Id, token);
            results.Add(imported);
        }

        return results.ToArray();
    }

    public async Task<ExerciseChallenge[]> ImportFromTrainingAsync(
        int courseId,
        int[]? challengeIds = null,
        CancellationToken token = default)
    {
        var query = context.ExerciseChallenges
            .Where(challenge => challenge.TrainingCourseId == courseId);
        if (challengeIds is { Length: > 0 })
            query = query.Where(challenge => challengeIds.Contains(challenge.Id));

        var sources = await query
            .Include(challenge => challenge.Attachment)
            .ThenInclude(attachment => attachment!.LocalFile)
            .Include(challenge => challenge.Flags)
            .ThenInclude(flag => flag.Attachment)
            .ThenInclude(attachment => attachment!.LocalFile)
            .ToArrayAsync(token);

        var results = new List<ExerciseChallenge>();
        foreach (var source in sources)
        {
            if (!CanCollect(source))
                continue;
            var existing = await context.ExerciseChallenges.FirstOrDefaultAsync(entry =>
                entry.PoolSource == ExercisePoolSource.Training &&
                entry.SourceChallengeId == source.Id && entry.TrainingCourseId == null, token);
            if (existing is not null)
            {
                results.Add(existing);
                continue;
            }

            await using var transaction = await context.Database.BeginTransactionAsync(token);
            var exercise = new ExerciseChallenge
            {
                Title = source.Title,
                Content = source.Content,
                Category = source.Category,
                Type = source.Type,
                Hints = source.Hints,
                IsEnabled = true,
                ContainerImage = source.ContainerImage,
                MemoryLimit = source.MemoryLimit,
                StorageLimit = source.StorageLimit,
                CPUCount = source.CPUCount,
                ExposePort = source.ExposePort,
                NetworkMode = source.NetworkMode,
                FileName = source.FileName,
                FlagTemplate = source.FlagTemplate,
                SubmissionLimit = source.SubmissionLimit,
                Environment = source.Environment,
                ImageTemplateId = source.ImageTemplateId,
                Difficulty = source.Difficulty,
                Tags = source.Tags?.ToList() ?? [source.Category.ToString()],
                Credit = source.Credit,
                PoolSource = ExercisePoolSource.Training,
                SourceTrainingCourseId = courseId,
                SourceChallengeId = source.Id,
                MinimumVisibleRole = Role.Teacher,
                Attachment = await CloneAttachmentAsync(source.Attachment, token)
            };
            foreach (var flag in source.Flags)
            {
                exercise.Flags.Add(new FlagContext
                {
                    Flag = flag.Flag,
                    Exercise = exercise,
                    IsOccupied = false,
                    OrderIndex = flag.OrderIndex,
                    Description = flag.Description,
                    ScoreMode = flag.ScoreMode,
                    FixedScore = flag.FixedScore,
                    MaxAttempts = flag.MaxAttempts,
                    AttachmentHash = flag.AttachmentHash,
                    AnswerType = flag.AnswerType,
                    CustomName = flag.CustomName,
                    Attachment = await CloneAttachmentAsync(flag.Attachment, token)
                });
            }
            await exerciseRepository.CreateExercise(exercise, token);
            await transaction.CommitAsync(token);
            results.Add(exercise);
        }
        return results.ToArray();
    }

    static bool CanCollect(Challenge challenge) =>
        challenge.Type.IsContainer() &&
        (!string.IsNullOrWhiteSpace(challenge.ContainerImage) || challenge.ImageTemplateId is not null) &&
        (challenge.Flags.Count > 0 || !string.IsNullOrWhiteSpace(challenge.FlagTemplate));
}
