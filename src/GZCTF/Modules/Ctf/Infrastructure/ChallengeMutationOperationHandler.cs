using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Ctf.Application;
using GZCTF.Modules.Ctf.Contracts;
using GZCTF.Modules.Ctf.Domain;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Ctf.Infrastructure;

public sealed class ChallengeMutationOperationHandler(
    AppDbContext context,
    ApiOperationService operations,
    IGameChallengeRepository challengeRepository,
    IContainerRepository containerRepository,
    DeploymentQueueService deploymentQueue,
    NodeExecutionGate executionGate,
    FleetVmService fleetVm,
    CacheHelper cache,
    ImageDistributionService distribution,
    ILogger<ChallengeMutationOperationHandler> logger) : IApiOperationHandler
{
    public string Kind => ChallengeExternalApplicationService.OperationKind;

    public async Task ExecuteAsync(
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var job = await context.Set<ChallengeMutationJob>().SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken)
            ?? throw new ApiOperationTerminalException(
                "challenge_job_not_found", "The persisted challenge operation payload was not found.");

        if (job.ResultJson is not null)
        {
            await CompletePostProcessingAsync(job, operationId, leaseOwner, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(job.PayloadJson))
            throw new ApiOperationTerminalException(
                "challenge_payload_missing", "The persisted challenge operation payload is unavailable.");

        try
        {
            switch (job.Kind)
            {
                case ChallengeMutationKind.Import:
                    await ImportAsync(job, operationId, leaseOwner, cancellationToken);
                    break;
                case ChallengeMutationKind.Delete:
                    await DeleteAsync(job, operationId, leaseOwner, cancellationToken);
                    break;
                default:
                    throw new ApiOperationTerminalException(
                        "challenge_operation_invalid", "The challenge operation kind is invalid.");
            }
        }
        catch (JsonException)
        {
            throw new ApiOperationTerminalException(
                "challenge_payload_invalid", "The persisted challenge operation payload is invalid.");
        }
    }

    public async Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var job = await context.Set<ChallengeMutationJob>().SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken);
        if (job is null)
            return;
        job.PayloadJson = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ImportAsync(
        ChallengeMutationJob job,
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ChallengeImportPayload>(
            job.PayloadJson!, ChallengeExternalApplicationService.JsonOptions)
            ?? throw new JsonException();
        await RequireLeaseAsync(
            operationId, leaseOwner, "challenge-validating", 0, 4, job.GameId, cancellationToken);

        var game = await context.Games.SingleOrDefaultAsync(
            item => item.Id == job.GameId, cancellationToken)
            ?? throw new ApiOperationTerminalException("game_not_found", "The target game no longer exists.");

        var windowsTemplateIds = payload.Items
            .Where(item => item.Environment == EnvironmentType.WindowsVM)
            .Select(item => item.ImageTemplateId!.Value)
            .Distinct()
            .ToArray();
        if (windowsTemplateIds.Length > 0)
        {
            var readyCount = await context.ImageTemplates.AsNoTracking().CountAsync(template =>
                windowsTemplateIds.Contains(template.Id) &&
                template.OSType == OSType.Windows &&
                template.ImageType != ImageType.Docker &&
                template.Status == ImageStatus.Ready,
                cancellationToken);
            if (readyCount != windowsTemplateIds.Length)
                throw new ApiOperationTerminalException(
                    "challenge_vm_template_invalid", "A referenced Windows VM template is no longer ready.");
        }

        var dockerTemplates = payload.Items
            .Where(item => item.Environment == EnvironmentType.Docker)
            .Select(item => new { TemplateId = item.ImageTemplateId!.Value, Image = item.ContainerImage! })
            .Distinct()
            .ToArray();
        if (dockerTemplates.Length > 0)
        {
            var templateIds = dockerTemplates.Select(item => item.TemplateId).ToArray();
            var readyTemplates = await context.ImageTemplates.AsNoTracking()
                .Where(template => templateIds.Contains(template.Id) &&
                                   template.ImageType == ImageType.Docker &&
                                   template.Status == ImageStatus.Ready)
                .Select(template => new { template.Id, template.RegistryUrl })
                .ToArrayAsync(cancellationToken);
            if (dockerTemplates.Any(item => !readyTemplates.Any(template =>
                    template.Id == item.TemplateId && template.RegistryUrl == item.Image)))
                throw new ApiOperationTerminalException(
                    "challenge_docker_image_unregistered",
                    "A referenced Docker image template is no longer ready.");
        }

        await RequireLeaseAsync(
            operationId, leaseOwner, "challenge-persisting", 1, 4, job.GameId, cancellationToken);
        var participationIds = payload.Items.Any(item => item.IsEnabled)
            ? await context.Participations.AsNoTracking()
                .Where(participation => participation.GameId == job.GameId)
                .Select(participation => participation.Id)
                .ToArrayAsync(cancellationToken)
            : [];
        var pending = new List<(OpenChallengeImportModel Item, GameChallenge Challenge)>(payload.Items.Count);

        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            foreach (var item in payload.Items)
            {
                var challenge = CreateChallenge(job.GameId, item);
                context.GameChallenges.Add(challenge);
                pending.Add((item, challenge));
                if (challenge.IsEnabled)
                {
                    context.GameInstances.AddRange(participationIds.Select(participationId => new GameInstance
                    {
                        ParticipationId = participationId,
                        Challenge = challenge
                    }));
                    if (game.IsActive)
                        context.GameNotices.Add(new GameNotice
                        {
                            GameId = game.Id,
                            Type = NoticeType.NewChallenge,
                            Values = [challenge.Title]
                        });
                }

            }

            await context.SaveChangesAsync(cancellationToken);
            var imported = pending.Select(item =>
                new OpenChallengeImportResultItem(item.Item.ExternalId, item.Challenge.Id)).ToArray();
            var result = new OpenChallengeMutationResult(job.GameId, imported, [], []);
            job.ResultJson = JsonSerializer.Serialize(result, ChallengeExternalApplicationService.JsonOptions);
            job.PayloadJson = null;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await CompletePostProcessingAsync(job, operationId, leaseOwner, cancellationToken);
    }

    private async Task DeleteAsync(
        ChallengeMutationJob job,
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ChallengeDeletePayload>(
            job.PayloadJson!, ChallengeExternalApplicationService.JsonOptions)
            ?? throw new JsonException();
        await RequireLeaseAsync(
            operationId, leaseOwner, "challenge-runtime-stopping", 0, 3, job.GameId, cancellationToken);

        if (!await context.Games.AsNoTracking().AnyAsync(game => game.Id == job.GameId, cancellationToken))
            throw new ApiOperationTerminalException("game_not_found", "The target game no longer exists.");

        await context.GameChallenges
            .Where(challenge => challenge.GameId == job.GameId && payload.ChallengeIds.Contains(challenge.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(challenge => challenge.IsEnabled, false),
                cancellationToken);

        var activeStatuses = new[]
        {
            DeploymentQueueTicketStatus.Pending,
            DeploymentQueueTicketStatus.Assigned,
            DeploymentQueueTicketStatus.Creating
        };
        var tickets = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.GameId == job.GameId &&
                             ticket.ChallengeId.HasValue &&
                             payload.ChallengeIds.Contains(ticket.ChallengeId.Value) &&
                             activeStatuses.Contains(ticket.Status))
            .Select(ticket => new { ticket.Id, ticket.TargetNodeId })
            .ToArrayAsync(cancellationToken);
        foreach (var ticket in tickets)
            await deploymentQueue.CancelAsync(ticket.Id, "Challenge deletion requested by external API.", cancellationToken);
        foreach (var nodeId in tickets.Select(ticket => ticket.TargetNodeId).OfType<Guid>().Distinct())
            await executionGate.RunExclusiveAsync(nodeId, _ => Task.CompletedTask, cancellationToken);

        context.ChangeTracker.Clear();
        var containers = await context.Containers
            .Include(container => container.GameInstance!)
                .ThenInclude(instance => instance.Challenge)
            .Include(container => container.GameInstance!)
                .ThenInclude(instance => instance.Participation)
            .Where(container => container.GameInstance != null &&
                                payload.ChallengeIds.Contains(container.GameInstance.ChallengeId))
            .ToArrayAsync(cancellationToken);
        foreach (var container in containers)
        {
            if (!await containerRepository.DestroyContainer(container, cancellationToken))
                throw new InvalidOperationException($"Container {container.Id} could not be destroyed.");
        }

        var testContainers = await context.GameChallenges
            .Include(challenge => challenge.TestContainer)
            .Where(challenge => challenge.GameId == job.GameId &&
                                payload.ChallengeIds.Contains(challenge.Id) &&
                                challenge.TestContainer != null)
            .Select(challenge => challenge.TestContainer!)
            .ToArrayAsync(cancellationToken);
        foreach (var container in testContainers)
        {
            if (!await containerRepository.DestroyContainer(container, cancellationToken))
                throw new InvalidOperationException($"Test container {container.Id} could not be destroyed.");
        }

        var virtualMachines = await context.VmInstances
            .Include(vm => vm.Challenge)
            .Where(vm => payload.ChallengeIds.Contains(vm.ChallengeId) &&
                         vm.Status != VmInstanceStatus.Destroyed)
            .ToArrayAsync(cancellationToken);
        foreach (var vm in virtualMachines)
            await fleetVm.DestroyVmAsync(vm, cancellationToken);

        await RequireLeaseAsync(
            operationId, leaseOwner, "challenge-deleting", 1, 3, job.GameId, cancellationToken);
        context.ChangeTracker.Clear();
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var persistedJob = await context.Set<ChallengeMutationJob>().SingleAsync(
                item => item.OperationId == job.OperationId, cancellationToken);
            var challenges = await context.GameChallenges
                .Where(challenge => challenge.GameId == job.GameId && payload.ChallengeIds.Contains(challenge.Id))
                .OrderBy(challenge => challenge.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var challenge in challenges)
                await challengeRepository.RemoveChallenge(challenge, false, cancellationToken);

            var deleted = challenges.Select(challenge => challenge.Id).ToArray();
            var missing = payload.ChallengeIds.Except(deleted).Order().ToArray();
            var result = new OpenChallengeMutationResult(job.GameId, [], deleted, missing);
            persistedJob.ResultJson = JsonSerializer.Serialize(result, ChallengeExternalApplicationService.JsonOptions);
            persistedJob.PayloadJson = null;
            persistedJob.CompletedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            job = persistedJob;
        }

        await CompletePostProcessingAsync(job, operationId, leaseOwner, cancellationToken);
    }

    private async Task CompletePostProcessingAsync(
        ChallengeMutationJob job,
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        if (job.Kind == ChallengeMutationKind.Import)
        {
            await RequireLeaseAsync(
                operationId, leaseOwner, "challenge-image-distributing", 3, 4, job.GameId, cancellationToken);
            try
            {
                await distribution.DistributeGameAsync(job.GameId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                                              !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "Challenge import {OperationId} completed, but image pre-distribution for game {GameId} is pending reconciliation",
                    operationId,
                    job.GameId);
            }
            await FlushScoreboardBestEffortAsync(job.GameId, operationId, cancellationToken);
            await UpdateProgressBestEffortAsync(
                operationId, leaseOwner, "challenges-imported", 4, 4, job.GameId, cancellationToken);
            return;
        }

        try
        {
            await distribution.ReleaseGameReferencesAsync(job.GameId, cancellationToken);
            await distribution.DistributeGameAsync(job.GameId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Challenge deletion {OperationId} completed, but image references for game {GameId} are pending reconciliation",
                operationId,
                job.GameId);
        }
        await FlushScoreboardBestEffortAsync(job.GameId, operationId, cancellationToken);
        await UpdateProgressBestEffortAsync(
            operationId, leaseOwner, "challenges-deleted", 3, 3, job.GameId, cancellationToken);
    }

    private async Task UpdateProgressBestEffortAsync(
        Guid operationId,
        string leaseOwner,
        string stage,
        long current,
        long total,
        int gameId,
        CancellationToken cancellationToken)
    {
        try
        {
            await operations.UpdateProgressAsync(
                operationId,
                leaseOwner,
                stage,
                current,
                total,
                "game",
                gameId.ToString(),
                null,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Challenge operation {OperationId} committed, but final progress could not be updated",
                operationId);
        }
    }

    private async Task FlushScoreboardBestEffortAsync(
        int gameId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await cache.FlushScoreboardCache(gameId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Challenge operation {OperationId} completed, but scoreboard cache invalidation for game {GameId} failed",
                operationId,
                gameId);
        }
    }

    private async Task RequireLeaseAsync(
        Guid operationId,
        string leaseOwner,
        string stage,
        long current,
        long total,
        int gameId,
        CancellationToken cancellationToken)
    {
        if (!await operations.UpdateProgressAsync(
                operationId,
                leaseOwner,
                stage,
                current,
                total,
                "game",
                gameId.ToString(),
                null,
                cancellationToken))
            throw new OperationCanceledException("The operation execution lease was lost.", cancellationToken);
    }

    private static GameChallenge CreateChallenge(int gameId, OpenChallengeImportModel item)
    {
        var challenge = new GameChallenge
        {
            GameId = gameId,
            Title = item.Title,
            Content = item.Content,
            Category = item.Category,
            Type = item.Type,
            Hints = item.Hints,
            IsEnabled = item.IsEnabled,
            DeadlineUtc = item.DeadlineUtc,
            SubmissionLimit = item.SubmissionLimit,
            OriginalScore = item.OriginalScore,
            MinScoreRate = item.MinScoreRate,
            Difficulty = item.Difficulty,
            DisableBloodBonus = item.DisableBloodBonus,
            FlagTemplate = item.FlagTemplate,
            Environment = item.Environment ?? EnvironmentType.None,
            ContainerImage = item.ContainerImage,
            ExposePort = item.ExposePort,
            ImageTemplateId = item.ImageTemplateId,
            CPUCount = item.CPUCount,
            MemoryLimit = item.MemoryLimit,
            StorageLimit = item.StorageLimit,
            NetworkMode = item.NetworkMode,
            EnableTrafficCapture = item.EnableTrafficCapture,
            FileName = item.FileName ?? "attachment",
            OsType = item.Environment == EnvironmentType.WindowsVM ? "Windows" : "Linux",
            Attachment = CreateAttachment(item.Attachment)
        };
        challenge.Flags = item.Flags.Select(flag => new FlagContext
        {
            Challenge = challenge,
            Flag = flag.Flag,
            OrderIndex = flag.OrderIndex,
            Description = flag.Description,
            ScoreMode = flag.ScoreMode,
            FixedScore = flag.FixedScore,
            MaxAttempts = flag.MaxAttempts,
            AttachmentHash = flag.AttachmentHash,
            AnswerType = flag.AnswerType,
            CustomName = flag.CustomName,
            Attachment = CreateAttachment(flag.Attachment)
        }).ToList();
        return challenge;
    }

    private static Attachment? CreateAttachment(OpenChallengeAttachmentModel? model) => model is null
        ? null
        : new Attachment { Type = FileType.Remote, RemoteUrl = model.RemoteUrl };
}
