using GZCTF.Repositories.Interface;
using GZCTF.Models.Request.Exercise;
using GZCTF.Models.Internal;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Exercise.Application;

public sealed class ExerciseService(
    AppDbContext context,
    IExerciseInstanceRepository instanceRepository,
    DeploymentQueueService deploymentQueue,
    IOptionsSnapshot<ContainerPolicy> containerPolicy) : IExerciseService
{
    public async Task<ExerciseChallenge[]> GetExercisesAsync(CancellationToken token = default) =>
        await context.ExerciseChallenges
            .AsNoTracking()
            .Where(e => e.IsEnabled && e.TrainingCourseId == null)
            .OrderBy(e => e.Id)
            .ToArrayAsync(token);

    public async Task<ExerciseChallenge?> GetExerciseByIdAsync(int exerciseId, CancellationToken token = default) =>
        await context.ExerciseChallenges
            .AsNoTracking()
            .Include(e => e.Flags).ThenInclude(flag => flag.Attachment).ThenInclude(attachment => attachment!.LocalFile)
            .Include(e => e.Attachment).ThenInclude(attachment => attachment!.LocalFile)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.IsEnabled && e.TrainingCourseId == null, token);

    public async Task<ExerciseInfoModel[]> GetExerciseListAsync(
        ExerciseFilter? filter,
        CancellationToken token = default,
        Guid? userId = null,
        Role role = Role.Student)
    {
        var query = context.ExerciseChallenges
            .AsNoTracking()
            .Where(e => e.IsEnabled && e.TrainingCourseId == null && e.MinimumVisibleRole <= role);

        if (filter is null)
            return await BuildInfoModels(query, userId, role >= Role.Teacher).ToArrayAsync(token);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.ToLower();
            query = query.Where(e =>
                EF.Functions.ILike(e.Title, $"%{search}%") ||
                EF.Functions.ILike(e.Content, $"%{search}%"));
        }

        if (filter.Categories is { Length: > 0 })
            query = query.Where(e => filter.Categories.Contains(e.Category));

        if (filter.Difficulties is { Length: > 0 })
            query = query.Where(e => filter.Difficulties.Contains(e.Difficulty));

        if (filter.Tags is { Length: > 0 })
            query = query.Where(e => e.Tags != null && e.Tags.Any(t => filter.Tags.Contains(t)));

        if (filter.Credit.HasValue)
            query = query.Where(e => e.Credit == filter.Credit.Value);

        if (filter.Sources is { Length: > 0 })
            query = query.Where(e => filter.Sources.Contains(e.PoolSource));

        return await BuildInfoModels(query, userId, role >= Role.Teacher).ToArrayAsync(token);
    }

    public async Task<ExerciseDetailModel?> GetExerciseDetailAsync(UserInfo user, int exerciseId, CancellationToken token = default)
    {
        var visible = await context.ExerciseChallenges.AsNoTracking().AnyAsync(exercise =>
            exercise.Id == exerciseId && exercise.IsEnabled && exercise.TrainingCourseId == null &&
            exercise.MinimumVisibleRole <= user.Role, token);
        if (!visible)
            return null;
        var instance = await instanceRepository.GetOrCreatePublicInstance(user, exerciseId, token);
        if (instance is null)
            return null;

        var submissions = context.ExerciseSubmissions
            .AsNoTracking()
            .Where(submission => submission.UserId == user.Id && submission.ExerciseChallengeId == exerciseId);
        var attempts = await submissions.CountAsync(token);
        var solvedFlagIds = await submissions
            .Where(submission => submission.Status == AnswerResult.Accepted && submission.FlagId != null)
            .Select(submission => submission.FlagId!.Value)
            .Distinct()
            .ToArrayAsync(token);
        var queue = await deploymentQueue.GetLatestSubjectStatusAsync(
            DeploymentQueueRequest.ExerciseContainer(user.Id, exerciseId), token);
        var model = ExerciseDetailModel.FromInstance(instance, attempts, solvedFlagIds, queue);
        PlayerRuntimeStatusProjection.Apply(model.Context, queue);
        return model;
    }

    public async Task<(AnswerResult Status, int? FlagId)> SubmitFlagAsync(UserInfo user, int exerciseId, string answer,
        int? flagId = null, string? ipAddress = null, CancellationToken token = default)
    {
        var instance = await instanceRepository.GetOrCreatePublicInstance(user, exerciseId, token);
        if (instance is null)
            return (AnswerResult.NotFound, null);

        var attempts = await context.ExerciseSubmissions.CountAsync(submission =>
            submission.UserId == user.Id && submission.ExerciseChallengeId == exerciseId, token);
        if (instance.Exercise.SubmissionLimit > 0 && attempts >= instance.Exercise.SubmissionLimit)
            return (AnswerResult.WrongAnswer, flagId);

        var result = await instanceRepository.VerifyAnswer(user, instance, answer, 0, flagId, token);
        if (result.Status == AnswerResult.NotFound)
            return result;

        context.ExerciseSubmissions.Add(new ExerciseSubmission
        {
            UserId = user.Id,
            ExerciseChallengeId = exerciseId,
            Status = result.Status,
            SubmittedAnswerHash = answer.ToSHA256String(),
            FlagId = result.FlagId,
            IpAddress = ipAddress ?? string.Empty
        });

        if (result.Status == AnswerResult.Accepted && result.FlagId.HasValue)
        {
            var requiredFlagIds = instance.Exercise.Type is ChallengeType.DynamicAttachment or ChallengeType.DynamicContainer
                ? [result.FlagId.Value]
                : await context.FlagContexts
                    .Where(flag => flag.ExerciseId == exerciseId)
                    .Select(flag => flag.Id)
                    .ToArrayAsync(token);
            var solvedFlagIds = await context.ExerciseSubmissions
                .Where(submission =>
                    submission.UserId == user.Id &&
                    submission.ExerciseChallengeId == exerciseId &&
                    submission.Status == AnswerResult.Accepted &&
                    submission.FlagId != null)
                .Select(submission => submission.FlagId!.Value)
                .Distinct()
                .ToArrayAsync(token);
            if (requiredFlagIds.All(required => required == result.FlagId.Value || solvedFlagIds.Contains(required)))
                instance.SolveTimeUtc = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(token);
        if (instance.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0))
            await instanceRepository.GetExerciseInstances(user, token);
        return result;
    }

    public async Task<TaskResult<Container>> CreateContainerAsync(UserInfo user, int exerciseId, CancellationToken token = default)
    {
        var instance = await instanceRepository.GetOrCreatePublicInstance(user, exerciseId, token);
        if (instance is null)
            return new TaskResult<Container>(TaskStatus.NotFound);
        if (!instance.Exercise.Type.IsContainer())
            return new TaskResult<Container>(TaskStatus.Denied);
        if (instance.IsContainerOperationTooFrequent)
            return new TaskResult<Container>(TaskStatus.Denied);

        return await instanceRepository.CreateContainer(instance, user, token);
    }

    public async Task<TaskResult<DeploymentQueueStatusModel>> ExtendContainerAsync(
        UserInfo user,
        int exerciseId,
        CancellationToken token = default)
    {
        var instance = await instanceRepository.GetOrCreatePublicInstance(user, exerciseId, token);
        if (instance is null)
            return new TaskResult<DeploymentQueueStatusModel>(TaskStatus.NotFound);
        if (!instance.Exercise.Type.IsContainer() || instance.Container is null)
            return new TaskResult<DeploymentQueueStatusModel>(TaskStatus.Denied);
        if (instance.Container.ExpectStopAt - DateTimeOffset.UtcNow >
            TimeSpan.FromMinutes(containerPolicy.Value.RenewalWindow))
            return new TaskResult<DeploymentQueueStatusModel>(TaskStatus.Denied);

        var queued = await deploymentQueue.EnqueueAsync(DeploymentQueueRequest.ExerciseContainer(
            user.Id, instance.ExerciseId) with
        {
            Operation = RuntimeOperationKind.Extend,
            TargetNodeId = instance.Container.NodeId,
            ExtensionSeconds = (int)TimeSpan.FromMinutes(containerPolicy.Value.ExtensionDuration).TotalSeconds,
            SubjectDisplayName = user.UserName,
            ResourceDisplayName = instance.Exercise.Title
        }, token);
        var status = await deploymentQueue.GetStatusAsync(queued.TicketId, token);
        return status is null
            ? new TaskResult<DeploymentQueueStatusModel>(TaskStatus.Failed)
            : new TaskResult<DeploymentQueueStatusModel>(TaskStatus.Success, status);
    }

    public async Task<TaskResult<DeploymentQueueStatusModel>> DestroyContainerAsync(
        UserInfo user,
        int exerciseId,
        CancellationToken token = default)
    {
        var instance = await instanceRepository.GetOrCreatePublicInstance(user, exerciseId, token);
        if (instance is null)
            return new TaskResult<DeploymentQueueStatusModel>(TaskStatus.NotFound);
        if (!instance.Exercise.Type.IsContainer() || instance.Container is null)
            return new TaskResult<DeploymentQueueStatusModel>(TaskStatus.Denied);
        if (instance.IsContainerOperationTooFrequent)
            return new TaskResult<DeploymentQueueStatusModel>(TaskStatus.Denied);

        var queued = await deploymentQueue.EnqueueAsync(DeploymentQueueRequest.ExerciseContainer(
            user.Id, instance.ExerciseId) with
        {
            Operation = RuntimeOperationKind.Stop,
            Generation = instance.Container.RuntimeGeneration,
            TargetNodeId = instance.Container.NodeId,
            SubjectDisplayName = user.UserName,
            ResourceDisplayName = instance.Exercise.Title
        }, token);
        instance.LastContainerOperation = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        var status = await deploymentQueue.GetStatusAsync(queued.TicketId, token);
        return status is null
            ? new TaskResult<DeploymentQueueStatusModel>(TaskStatus.Failed)
            : new TaskResult<DeploymentQueueStatusModel>(TaskStatus.Success, status);
    }

    IQueryable<ExerciseInfoModel> BuildInfoModels(
        IQueryable<ExerciseChallenge> query,
        Guid? userId,
        bool includeCreator) =>
        query.Select(e => new ExerciseInfoModel
        {
            Id = e.Id,
            Title = e.Title,
            Difficulty = e.Difficulty,
            Category = e.Category,
            Type = e.Type,
            IsEnabled = e.IsEnabled,
            Tags = e.Tags ?? new(),
            Credit = e.Credit,
            PoolSource = e.PoolSource,
            CreatorUserName = includeCreator ? e.CreatedBy!.UserName : null,
            AcceptedCount = context.ExerciseInstances.Count(i =>
                i.ExerciseId == e.Id && i.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0)),
            SubmissionCount = context.ExerciseSubmissions.Count(submission =>
                submission.ExerciseChallengeId == e.Id),
            Solved = userId.HasValue && context.ExerciseInstances.Any(instance =>
                instance.UserId == userId.Value &&
                instance.ExerciseId == e.Id &&
                instance.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0)),
            UserAcceptedCount = userId.HasValue
                ? context.ExerciseSubmissions.Count(submission =>
                    submission.UserId == userId.Value &&
                    submission.ExerciseChallengeId == e.Id &&
                    submission.Status == AnswerResult.Accepted)
                : 0,
            UserSubmissionCount = userId.HasValue
                ? context.ExerciseSubmissions.Count(submission =>
                    submission.UserId == userId.Value && submission.ExerciseChallengeId == e.Id)
                : 0
        });
}
