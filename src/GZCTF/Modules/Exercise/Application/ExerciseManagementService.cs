using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Exercise.Application;

public sealed class ExerciseManagementService(
    AppDbContext context,
    IExerciseChallengeRepository exerciseRepository,
    IBlobRepository blobRepository,
    IGameChallengeRepository gameChallengeRepository) : IExerciseManagementService
{
    public async Task<ExerciseChallenge> CreateExerciseAsync(ExerciseChallenge exercise, CancellationToken token = default) =>
        await exerciseRepository.CreateExercise(exercise, token);

    public async Task<ExerciseChallenge> UpdateExerciseAsync(ExerciseChallenge exercise, CancellationToken token = default)
    {
        context.ExerciseChallenges.Update(exercise);
        await context.SaveChangesAsync(token);
        return exercise;
    }

    public async Task<ExerciseChallenge?> GetExerciseForUpdateAsync(int exerciseId, CancellationToken token = default) =>
        await context.ExerciseChallenges
            .Include(e => e.Flags)
            .Include(e => e.Attachment)
            .FirstOrDefaultAsync(e => e.Id == exerciseId, token);

    public async Task<ExerciseChallenge> UpdateExerciseWithRelationsAsync(
        ExerciseChallenge exercise,
        List<ExerciseOpenApiFlagModel>? flags,
        ExerciseOpenApiAttachmentModel? attachment,
        CancellationToken token = default)
    {
        var existing = await context.ExerciseChallenges
            .Include(e => e.Flags)
            .Include(e => e.Attachment)
            .FirstOrDefaultAsync(e => e.Id == exercise.Id, token)
            ?? throw new InvalidOperationException($"Exercise {exercise.Id} not found");

        context.Entry(existing).CurrentValues.SetValues(exercise);

        if (flags is not null)
        {
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
            if (existing.Attachment is not null)
                context.Attachments.Remove(existing.Attachment);
            existing.Attachment = CreateAttachment(attachment);
        }

        await context.SaveChangesAsync(token);
        return existing;
    }

    static Attachment? CreateAttachment(ExerciseOpenApiAttachmentModel? model) => model is null
        ? null
        : new Attachment { Type = FileType.Remote, RemoteUrl = model.RemoteUrl };

    public async Task RemoveExerciseAsync(int exerciseId, CancellationToken token = default)
    {
        var exercise = await context.ExerciseChallenges
            .Include(e => e.Attachment)
            .FirstOrDefaultAsync(e => e.Id == exerciseId, token);

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
            .FirstOrDefaultAsync(gc => gc.Id == gameChallengeId, token)
            ?? throw new InvalidOperationException($"Game challenge {gameChallengeId} not found");

        var existing = await context.ExerciseChallenges
            .FirstOrDefaultAsync(e => e.Title == gameChallenge.Title && e.TrainingCourseId == null, token);

        if (existing is not null)
            return existing;

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
            Difficulty = Difficulty.Normal,
            Tags = [gameChallenge.Category.ToString()],
            Credit = false,
            AttachmentId = gameChallenge.AttachmentId
        };

        await exerciseRepository.CreateExercise(exercise, token);

        foreach (var flag in gameChallenge.Flags)
        {
            context.FlagContexts.Add(new FlagContext
            {
                Flag = flag.Flag,
                ExerciseId = exercise.Id,
                IsOccupied = true,
                OrderIndex = flag.OrderIndex,
                Description = flag.Description,
                ScoreMode = flag.ScoreMode,
                FixedScore = flag.FixedScore,
                MaxAttempts = flag.MaxAttempts,
                AttachmentHash = flag.AttachmentHash,
                AnswerType = flag.AnswerType,
                CustomName = flag.CustomName
            });
        }

        await context.SaveChangesAsync(token);
        return exercise;
    }

    public async Task<ExerciseChallenge[]> ImportFromGameAsync(int gameId, int[]? challengeIds = null, CancellationToken token = default)
    {
        var query = context.GameChallenges
            .Where(gc => gc.GameId == gameId);

        if (challengeIds is { Length: > 0 })
            query = query.Where(gc => challengeIds.Contains(gc.Id));

        var challenges = await query.ToArrayAsync(token);
        var results = new List<ExerciseChallenge>();

        foreach (var challenge in challenges)
        {
            var imported = await ImportFromGameChallengeAsync(challenge.Id, token);
            results.Add(imported);
        }

        return results.ToArray();
    }
}
