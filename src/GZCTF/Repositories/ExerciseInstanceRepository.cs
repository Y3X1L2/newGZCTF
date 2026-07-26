using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using GZCTF.Infrastructure.Cache;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace GZCTF.Repositories;

[ExcludeFromCodeCoverage(Justification = "Exercise feature not yet implemented")]
public class ExerciseInstanceRepository(
    AppDbContext context,
    IPlatformCache platformCache,
    IContainerManager service,
    IContainerRepository containerRepository,
    IOptionsSnapshot<ContainerPolicy> containerPolicy,
    DockerImageRegistryService dockerRegistry,
    INginxProxySyncService nginxProxySync,
    DeploymentQueueService deploymentQueue,
    DeploymentExecutionContextAccessor deploymentExecutionContext,
    IDistributedLeaseProvider lockService,
    ILogger<ExerciseInstanceRepository> logger,
    IStringLocalizer<Program> localizer
) : RepositoryBase(context),
    IExerciseInstanceRepository
{
    static readonly DeploymentQueueTicketStatus[] ActiveQueueStatuses =
    [
        DeploymentQueueTicketStatus.Pending,
        DeploymentQueueTicketStatus.Scheduling,
        DeploymentQueueTicketStatus.Scheduled,
        DeploymentQueueTicketStatus.Running
    ];

    public async Task<ExerciseInstance[]> GetExerciseInstances(UserInfo user, CancellationToken token = default)
    {
        if (!await IsExerciseAvailable(token))
            return [];

        var exercises = await Context.ExerciseInstances
            .Where(i => i.UserId == user.Id && i.Exercise.IsEnabled)
            .ToArrayAsync(token);

        if (exercises.Length > 0)
            return exercises;

        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        var result = new List<ExerciseInstance>();

        await foreach (var id in Context.ExerciseChallenges
                           .Where(e => e.IsEnabled && e.TrainingCourseId == null &&
                                       Context.ExerciseDependencies.All(d => d.TargetId != e.Id))
                           .Select(e => e.Id).AsAsyncEnumerable().WithCancellation(token))
        {
            var newInst = new ExerciseInstance { ExerciseId = id, UserId = user.Id, IsLoaded = false };

            Context.ExerciseInstances.Add(newInst);
            result.Add(newInst);
        }

        await SaveAsync(token);
        await transaction.CommitAsync(token);

        return result.ToArray();
    }

    public async Task<ExerciseInstance?> GetInstance(UserInfo user, int exerciseId, CancellationToken token = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        var instance = await Context.ExerciseInstances
            .Include(i => i.FlagContext)
            .Include(i => i.Container)
            .Include(i => i.Exercise)
            .Where(e => e.ExerciseId == exerciseId && e.UserId == user.Id)
            .SingleOrDefaultAsync(token);

        // we assume that the user has no permission to access the challenge
        // if the instance does not exist
        if (instance is null)
            return null;

        if (!instance.Exercise.Type.IsDynamic())
            await Context.Entry(instance.Exercise).Collection(e => e.Flags).LoadAsync(token);

        if (instance.IsLoaded)
        {
            await transaction.CommitAsync(token);
            return instance;
        }

        var exercise = instance.Exercise;

        if (!exercise.IsEnabled)
        {
            await transaction.CommitAsync(token);
            return null;
        }

        try
        {
            switch (instance.Exercise.Type)
            {
                case ChallengeType.DynamicContainer:
                    instance.FlagContext = FlagContext.CreateInstanceFlag(
                        exercise.GenerateDynamicFlag());
                    break;
                case ChallengeType.DynamicAttachment:
                    var flags = await Context.FlagContexts
                        .Where(e => e.ExerciseId == exercise.Id && !e.IsOccupied)
                        .ToListAsync(token);

                    if (flags.Count == 0)
                    {
                        logger.SystemLog(
                            StaticLocalizer[nameof(Resources.Program.InstanceRepository_DynamicFlagsNotEnough),
                                exercise.Title,
                                exercise.Id], TaskStatus.Failed,
                            LogLevel.Warning);
                        await transaction.RollbackAsync(token);
                        return null;
                    }

                    var pos = Random.Shared.Next(flags.Count);
                    flags[pos].IsOccupied = true;
                    instance.FlagId = flags[pos].Id;
                    break;
            }

            // instance.FlagContext is null by default
            // static flag does not need to be dispatched

            instance.IsLoaded = true;
            await SaveAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            logger.SystemLog(
                localizer[nameof(Resources.Program.InstanceRepository_GetInstanceFailed), user.UserName!,
                    exercise.Title, exercise.Id],
                TaskStatus.Failed, LogLevel.Warning);
            await transaction.RollbackAsync(token);
            return null;
        }

        return instance;
    }

    public async Task<TaskResult<Container>> CreateContainer(ExerciseInstance instance, UserInfo user,
        CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(instance.Exercise.ContainerImage) || instance.Exercise.ExposePort is null)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreationFailed),
                    instance.Exercise.Title],
                TaskStatus.Denied, LogLevel.Warning);
            return new TaskResult<Container>(TaskStatus.Failed);
        }

        await using var instanceLock = await lockService.AcquireAsync(
            BuildInstanceRuntimeLockKey(user.Id, instance.ExerciseId),
            TimeSpan.FromSeconds(10),
            cancellationToken: token);
        using var instanceLeaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            token,
            instanceLock.LeaseLost);
        token = instanceLeaseCancellation.Token;

        await ReloadInstanceRuntimeAsync(instance, token);

        if (instance.Container?.IsActiveAt(DateTimeOffset.UtcNow) == true)
            return new TaskResult<Container>(TaskStatus.Success, instance.Container);

        if (instance.Container is not null)
        {
            if (!await containerRepository.DestroyContainer(instance.Container, token))
                return new TaskResult<Container>(TaskStatus.Failed);

            instance.Container = null;
            instance.ContainerId = null;
        }

        await RegenerateLegacyDynamicFlagAsync(instance, token);

        await using var ownerLock = await lockService.AcquireAsync(
            BuildContainerLimitLockKey(user.Id),
            TimeSpan.FromSeconds(10),
            cancellationToken: token);
        using var ownerLeaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, ownerLock.LeaseLost);
        token = ownerLeaseCancellation.Token;

        await ReloadInstanceRuntimeAsync(instance, token);

        if (instance.Container is not null)
            return new TaskResult<Container>(TaskStatus.Success, instance.Container);

        // containerLimit == 0 means unlimited
        var containerLimit = containerPolicy.Value.MaxExerciseContainerCountPerUser;
        if (containerLimit > 0)
        {
            var queuedCount = await CountActiveQueuedContainersAsync(user.Id, instance.ExerciseId, token);
            var running = await Context.ExerciseInstances
                .Include(i => i.Exercise)
                .Include(i => i.Container)
                .Where(i => i.UserId == user.Id && i.ContainerId != null)
                .OrderBy(i => i.Container!.StartedAt)
                .ToListAsync(token);

            var first = running.FirstOrDefault();
            var allowedRunningBeforeCreate = containerLimit - queuedCount;
            if (allowedRunningBeforeCreate <= 0)
                return new TaskResult<Container>(TaskStatus.Denied);

            if (running.Count >= allowedRunningBeforeCreate && first is not null)
            {
                logger.Log(
                    StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerAutoDestroy),
                        user.UserName!, first.Exercise.Title,
                        first.Container!.LogId],
                    user, TaskStatus.Success);
                if (!await containerRepository.DestroyContainer(first.Container!, token))
                    return new TaskResult<Container>(TaskStatus.Failed);
            }
        }

        if (deploymentExecutionContext.Current is null)
        {
            var request = instance.Exercise.TrainingCourseId.HasValue
                ? DeploymentQueueRequest.TrainingContainer(user.Id, instance.ExerciseId)
                : DeploymentQueueRequest.ExerciseContainer(user.Id, instance.ExerciseId);
            var queued = await deploymentQueue.EnqueueAsync(request with
            {
                SubjectType = instance.Exercise.TrainingCourseId.HasValue
                    ? "training-container"
                    : "exercise-container",
                SubjectPublicId = $"{user.Id:D}:{instance.ExerciseId}",
                SubjectDisplayName = user.UserName,
                ResourceDisplayName = instance.Exercise.Title
            }, token);
            var queueStatus = await deploymentQueue.GetStatusAsync(queued.TicketId, token)
                              ?? throw new InvalidOperationException("Queued exercise ticket could not be loaded.");
            return new QueuedTaskResult<Container>(queueStatus);
        }

        var image = await dockerRegistry.ResolveImageReferenceAsync(instance.Exercise.ContainerImage, token);
        var container = await service.CreateContainerAsync(new ContainerConfig
        {
            Generation = deploymentExecutionContext.Current?.Generation ?? 1,
            TeamId = "exercise",
            UserId = user.Id,
            ChallengeId = instance.ExerciseId,
            PreferredNodeId = deploymentExecutionContext.Current?.TargetNodeId,
            FleetCapacityReserved = deploymentExecutionContext.Current?.CapacityReserved == true,
            Flag = instance.FlagContext?.Flag, // static challenge has no specific flag
            Image = image,
            CPUCount = instance.Exercise.CPUCount ?? 1,
            MemoryLimit = instance.Exercise.MemoryLimit ?? 64,
            StorageLimit = instance.Exercise.StorageLimit ?? 256,
            NetworkMode = instance.Exercise.NetworkMode ?? NetworkMode.Open,
            EnableTrafficCapture = false,
            ExposedPort = instance.Exercise.ExposePort.Value
        }, token);

        if (container is null)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreationFailed),
                    instance.Exercise.Title],
                TaskStatus.Failed, LogLevel.Warning);
            return new TaskResult<Container>(TaskStatus.Failed);
        }

        instance.Container = container;
        instance.LastContainerOperation = DateTimeOffset.UtcNow;

        logger.Log(
            StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreated), user.UserName!,
                instance.Exercise.Title,
                container.LogId], user,
            TaskStatus.Success);

        await SaveAsync(token);
        await nginxProxySync.TrySyncNowAsync("exercise container created", token);

        return new TaskResult<Container>(TaskStatus.Success, instance.Container);
    }

    static string BuildContainerLimitLockKey(Guid userId) =>
        $"container-limit:exercise:user:{userId}";

    static string BuildInstanceRuntimeLockKey(Guid userId, int exerciseId) =>
        $"container-runtime:exercise:user:{userId}:challenge:{exerciseId}";

    async Task ReloadInstanceRuntimeAsync(ExerciseInstance instance, CancellationToken token)
    {
        await Context.Entry(instance).ReloadAsync(token);

        if (instance.ContainerId is null)
        {
            instance.Container = null;
        }
        else if (instance.Container?.Id == instance.ContainerId)
        {
            await Context.Entry(instance.Container).ReloadAsync(token);
        }
        else
        {
            instance.Container = await Context.Containers
                .SingleOrDefaultAsync(container => container.Id == instance.ContainerId, token);
        }

        if (instance.FlagId is null)
        {
            instance.FlagContext = null;
        }
        else if (instance.FlagContext?.Id == instance.FlagId)
        {
            await Context.Entry(instance.FlagContext).ReloadAsync(token);
        }
        else
        {
            instance.FlagContext = await Context.FlagContexts
                .SingleOrDefaultAsync(flag => flag.Id == instance.FlagId, token);
        }
    }

    async Task RegenerateLegacyDynamicFlagAsync(ExerciseInstance instance, CancellationToken token)
    {
        if (!instance.TryRegenerateLegacyDynamicFlag())
            return;

        await SaveAsync(token);
    }

    async Task<int> CountActiveQueuedContainersAsync(Guid userId, int currentExerciseId, CancellationToken token) =>
        await Context.DeploymentQueueTickets.CountAsync(t =>
            t.Kind == DeploymentQueueKind.ExerciseContainer &&
            t.OwnerUserId == userId &&
            t.ChallengeId != currentExerciseId &&
            ActiveQueueStatuses.Contains(t.Status), token);

    public async Task<(AnswerResult Status, int? FlagId)> VerifyAnswer(UserInfo user, ExerciseInstance instance, string answer,
        int courseId,
        int? flagId = null,
        CancellationToken token = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        var exercise = await Context.ExerciseChallenges
            .AsNoTracking()
            .Select(c => new { c.Id, c.Type })
            .SingleAsync(c => c.Id == instance.ExerciseId, token);

        FlagContext? targetFlag;
        if (exercise.Type == ChallengeType.DynamicContainer && instance.FlagContext is not null)
        {
            targetFlag = instance.FlagContext;
        }
        else if (exercise.Type == ChallengeType.DynamicAttachment && instance.FlagId.HasValue)
        {
            targetFlag = await Context.FlagContexts
                .FirstOrDefaultAsync(f => f.Id == instance.FlagId.Value && f.ExerciseId == instance.ExerciseId, token);
        }
        else if (flagId.HasValue)
        {
            targetFlag = await Context.FlagContexts
                .FirstOrDefaultAsync(f => f.Id == flagId.Value && f.ExerciseId == instance.ExerciseId, token);
        }
        else
        {
            targetFlag = await Context.FlagContexts
                .Where(f => f.ExerciseId == instance.ExerciseId)
                .OrderBy(f => f.OrderIndex)
                .FirstOrDefaultAsync(token);
        }

        if (targetFlag is null)
        {
            await transaction.RollbackAsync(token);
            return (AnswerResult.NotFound, null);
        }

        if (targetFlag.MaxAttempts > 0)
        {
            var attemptCount = await Context.TrainingCourseSubmissions.CountAsync(s =>
                s.UserId == user.Id &&
                s.CourseId == courseId &&
                s.ExerciseChallengeId == instance.ExerciseId &&
                s.FlagId == targetFlag.Id, token);
            if (attemptCount >= targetFlag.MaxAttempts)
            {
                await transaction.RollbackAsync(token);
                return (AnswerResult.WrongAnswer, targetFlag.Id);
            }
        }

        var isCorrect = targetFlag.AnswerType switch
        {
            AnswerType.File => string.Equals(answer.ToSHA256String(), targetFlag.AttachmentHash,
                StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(targetFlag.Flag, answer, StringComparison.Ordinal)
        };

        if (!isCorrect)
        {
            await transaction.CommitAsync(token);
            return (AnswerResult.WrongAnswer, targetFlag.Id);
        }

        if (instance.SolveTimeUtc <= DateTimeOffset.FromUnixTimeSeconds(0))
            instance.SolveTimeUtc = DateTimeOffset.UtcNow;

        await foreach (var id in FetchNewChallenges(user, token))
        {
            var newInst = new ExerciseInstance { ExerciseId = id, UserId = user.Id, IsLoaded = false };
            Context.ExerciseInstances.Add(newInst);
        }

        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return (AnswerResult.Accepted, targetFlag.Id);
    }

    private Task<bool> IsExerciseAvailable(CancellationToken token = default) =>
        platformCache.GetOrCreateAsync(CachePolicyCatalog.ExerciseAvailability, "global",
            async ct => await Context.ExerciseChallenges.AnyAsync(
                e => e.IsEnabled && e.TrainingCourseId == null, ct), token).AsTask();

    internal async Task MarkSolved(ExerciseInstance instance, CancellationToken token = default)
    {
        if (instance.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0))
            return;

        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        instance.SolveTimeUtc = DateTimeOffset.UtcNow;
        await SaveAsync(token);

        await transaction.CommitAsync(token);
    }

    internal async Task UnlockExercises(UserInfo user, CancellationToken token = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        await foreach (var id in FetchNewChallenges(user, token))
        {
            var newInst = new ExerciseInstance { ExerciseId = id, UserId = user.Id, IsLoaded = false };
            Context.ExerciseInstances.Add(newInst);
        }

        await SaveAsync(token);
        await transaction.CommitAsync(token);
    }

    internal ConfiguredCancelableAsyncEnumerable<int> FetchNewChallenges(UserInfo user,
        CancellationToken token = default)
        => Context.ExerciseChallenges.Where(chal =>
                chal.IsEnabled && chal.TrainingCourseId == null && !Context.ExerciseInstances.Any(i =>
                    i.UserId == user.Id && i.ExerciseId == chal.Id) &&
                Context.ExerciseDependencies
                    .Where(dep => dep.TargetId == chal.Id)
                    .All(dep => Context.ExerciseInstances.Any(e =>
                        e.UserId == user.Id &&
                        e.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0) &&
                        e.ExerciseId == dep.SourceId
                    ))).Select(e => e.Id).AsAsyncEnumerable()
            .WithCancellation(token);
}
