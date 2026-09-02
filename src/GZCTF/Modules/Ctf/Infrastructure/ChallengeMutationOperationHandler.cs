using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Ctf.Application;
using GZCTF.Modules.Ctf.Contracts;
using GZCTF.Modules.Ctf.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Exercise.Application;
using GZCTF.Repositories.Interface;
using GZCTF.Infrastructure.Cache;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Ctf.Infrastructure;

public sealed class ChallengeMutationOperationHandler(
    AppDbContext context,
    ApiOperationService operations,
    IGameChallengeRepository challengeRepository,
    DeploymentQueueService deploymentQueue,
    NodeDispatchLimiter dispatchLimiter,
    IPlatformCache cache,
    ImageDistributionService distribution,
    IExerciseManagementService exerciseManagement,
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
                case ChallengeMutationKind.ImportAwdp:
                    await ImportAwdpAsync(job, operationId, leaseOwner, cancellationToken);
                    break;
                case ChallengeMutationKind.DeleteAwdp:
                    await DeleteAwdpAsync(job, operationId, leaseOwner, cancellationToken);
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

    private async Task ImportAwdpAsync(
        ChallengeMutationJob job, Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<AwdpImportPayload>(job.PayloadJson!, ChallengeExternalApplicationService.JsonOptions)
            ?? throw new JsonException();
        await RequireLeaseAsync(operationId, leaseOwner, "awdp-persisting", 0, payload.Items.Count, job.GameId, cancellationToken);
        var game = await context.Games.SingleOrDefaultAsync(item => item.Id == job.GameId, cancellationToken)
            ?? throw new ApiOperationTerminalException("game_not_found", "The target game no longer exists.");
        if (game.GameType is not GameType.AWDP and not GameType.Mixed)
            throw new ApiOperationTerminalException("awdp_game_required", "The target game is not an AWDP or mixed game.");

        var imported = new List<OpenAwdpServiceImportResultItem>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in payload.Items)
        {
            var service = await context.AwdpServices.SingleOrDefaultAsync(existing =>
                existing.GameId == job.GameId && existing.ExternalId == item.ExternalId, cancellationToken);
            service ??= new AwdpService { GameId = job.GameId, ExternalId = item.ExternalId };
            service.Name = item.Name;
            service.Content = item.Content;
            service.Category = item.Category;
            service.Difficulty = item.Difficulty;
            service.Tags = item.Tags;
            service.FlagTemplate = item.FlagTemplate;
            service.ImageName = item.ImageName;
            service.ExposePort = item.ExposePort;
            service.CheckerScript = item.CheckerScript;
            service.CheckerEntrypoint = item.CheckerEntrypoint;
            service.ExpScript = item.ExpScript;
            service.ExpEntrypoint = item.ExpEntrypoint;
            service.OriginalScore = item.OriginalScore;
            service.AttackPoints = item.AttackPoints;
            service.SlaPoints = item.SlaPoints;
            service.PatchPoints = item.PatchPoints;
            service.ServiceAbnormalPenalty = item.ServiceAbnormalPenalty;
            service.MaxAttackPerRound = item.MaxAttackPerRound;
            service.AttackPhaseMinutes = item.AttackPhaseMinutes;
            service.PatchPhaseMinutes = item.PatchPhaseMinutes;
            service.TotalRounds = item.TotalRounds;
            service.MaxResetCount = item.MaxResetCount;
            service.MaxRecoveryCount = item.MaxRecoveryCount;
            if (service.Id == 0)
                context.AwdpServices.Add(service);
            await context.SaveChangesAsync(cancellationToken);
            await exerciseManagement.CollectAwdpServiceAsync(service.Id, cancellationToken);
            imported.Add(new OpenAwdpServiceImportResultItem(item.ExternalId, service.Id));
        }
        var result = new OpenChallengeMutationResult(job.GameId, [], [], [], imported);
        job.ResultJson = JsonSerializer.Serialize(result, ChallengeExternalApplicationService.JsonOptions);
        job.PayloadJson = null;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await CompletePostProcessingAsync(job, operationId, leaseOwner, cancellationToken);
    }

    private async Task DeleteAwdpAsync(
        ChallengeMutationJob job, Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<AwdpDeletePayload>(job.PayloadJson!, ChallengeExternalApplicationService.JsonOptions)
            ?? throw new JsonException();
        await RequireLeaseAsync(operationId, leaseOwner, "awdp-deleting", 0, payload.ServiceIds.Count, job.GameId, cancellationToken);
        var services = await context.AwdpServices
            .Where(item => item.GameId == job.GameId && payload.ServiceIds.Contains(item.Id))
            .ToArrayAsync(cancellationToken);
        var deleted = services.Select(item => item.Id).ToArray();
        foreach (var service in services)
        {
            await context.ExerciseChallenges
                .Where(item => item.SourceAwdpServiceId == service.Id && item.TrainingCourseId == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, false), cancellationToken);
            context.AwdpServices.Remove(service);
        }
        await context.SaveChangesAsync(cancellationToken);
        var result = new OpenChallengeMutationResult(job.GameId, [], [], payload.ServiceIds.Except(deleted).ToArray(), [], deleted);
        job.ResultJson = JsonSerializer.Serialize(result, ChallengeExternalApplicationService.JsonOptions);
        job.PayloadJson = null;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await CompletePostProcessingAsync(job, operationId, leaseOwner, cancellationToken);
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
        var actorUserId = await context.ApiOperations
            .Where(operation => operation.Id == operationId)
            .Select(operation => operation.ActorUserId)
            .SingleOrDefaultAsync(cancellationToken);

        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            foreach (var item in payload.Items)
            {
                var challenge = CreateChallenge(job.GameId, item);
                challenge.CreatedById = actorUserId;
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
            foreach (var item in pending)
                await exerciseManagement.CollectGameChallengeAsync(item.Challenge.Id, cancellationToken);
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
            DeploymentQueueTicketStatus.Scheduling,
            DeploymentQueueTicketStatus.Scheduled,
            DeploymentQueueTicketStatus.Running
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
            await dispatchLimiter.WaitForIdleAsync(nodeId, NodeDispatchCategory.DockerCreate, cancellationToken);

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
            var queued = await deploymentQueue.EnqueueAsync(
                DeploymentQueueRequest.MaintenanceContainer(
                    container.Id, container.NodeId, container.Image, container.RuntimeGeneration),
                cancellationToken);
            await RequireQueueSuccessAsync(queued.TicketId, cancellationToken);
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
            var queued = await deploymentQueue.EnqueueAsync(
                DeploymentQueueRequest.MaintenanceContainer(
                    container.Id, container.NodeId, container.Image, container.RuntimeGeneration),
                cancellationToken);
            await RequireQueueSuccessAsync(queued.TicketId, cancellationToken);
        }

        var virtualMachines = await context.VmInstances
            .Include(vm => vm.Challenge)
            .Where(vm => payload.ChallengeIds.Contains(vm.ChallengeId) &&
                         vm.Status != VmInstanceStatus.Destroyed)
            .ToArrayAsync(cancellationToken);
        foreach (var vm in virtualMachines)
        {
            if (vm.Challenge is null)
                throw new InvalidOperationException($"VM {vm.Id} has no challenge metadata.");
            var queued = await deploymentQueue.EnqueueAsync(DeploymentQueueRequest.Vm(
                vm.Challenge.GameId, vm.UserId, vm.ChallengeId, vm.Id) with
            {
                Operation = RuntimeOperationKind.Destroy,
                Generation = vm.RuntimeGeneration,
                TargetNodeId = vm.NodeId,
                SubjectDisplayName = "Challenge deletion",
                ResourceDisplayName = vm.VmName
            }, cancellationToken);
            await RequireQueueSuccessAsync(queued.TicketId, cancellationToken);
        }

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
            await cache.InvalidateAsync(CachePolicyCatalog.Scoreboard, gameId.ToString(), cancellationToken);
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

    private async Task RequireQueueSuccessAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await deploymentQueue.GetStatusAsync(ticketId, cancellationToken);
            if (status?.Status == DeploymentQueueTicketStatus.Succeeded)
                return;
            if (status?.Status is DeploymentQueueTicketStatus.Failed or DeploymentQueueTicketStatus.Cancelled)
                throw new InvalidOperationException(status.ErrorMessage ?? $"Runtime control ticket {ticketId} failed.");
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException($"Runtime control ticket {ticketId} did not complete in time.");
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
