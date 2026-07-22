using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class ImageDistributionService(
    AppDbContext context,
    AgentClient agentClient,
    DockerImageRegistryService dockerRegistry,
    VmArtifactStore vmArtifacts,
    ImageDistributionCoordinator coordinator,
    DeploymentExecutionContextAccessor executionContext,
    IOperationalEventWriter events,
    ILogger<ImageDistributionService> logger)
{
    static readonly NodeCapability DockerCapability = NodeCapability.Docker;
    static readonly NodeCapability VmCapability = NodeCapability.Kvm;

    public async Task<IReadOnlyList<ImageDistributionRecord>> DistributeToCapableNodesAsync(
        ImageTemplate template,
        CancellationToken token,
        ImageDistributionReferenceKey? reference = null)
    {
        var persisted = context.Entry(template).State == EntityState.Detached
            ? await context.ImageTemplates.SingleOrDefaultAsync(item => item.Id == template.Id, token)
            : template;
        if (persisted is null)
            throw new InvalidOperationException($"Image template {template.Id} was not found.");
        if (persisted.Status == ImageStatus.Deleting)
            throw new InvalidOperationException($"Image template {template.Id} is being deleted.");

        return await DistributeTemplateAsync(template.Id, reference, token);
    }

    public async Task<IReadOnlyList<ImageDistributionRecord>> DistributeTemplateAsync(
        int templateId,
        ImageDistributionReferenceKey? reference,
        CancellationToken token)
    {
        var template = await context.ImageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, token);
        if (template is null || template.Status == ImageStatus.Deleting)
            return [];

        var nodes = await GetCapableNodesAsync(template, token);
        List<ImageDistributionRecord> records = [];
        foreach (var node in nodes)
            records.Add(await QueueTemplateOnNodeAsync(template, node, reference, token));
        if (records.Count > 0)
            coordinator.Wake();
        return records;
    }

    public async Task DistributeGameAsync(int gameId, CancellationToken token)
    {
        var templateIds = await context.GameChallenges.AsNoTracking()
            .Where(c => c.GameId == gameId &&
                        (c.Type == ChallengeType.StaticContainer ||
                         c.Type == ChallengeType.DynamicContainer))
            .Select(c => c.ImageTemplateId)
            .ToArrayAsync(token);

        var dockerImages = await context.GameChallenges.AsNoTracking()
            .Where(c => c.GameId == gameId &&
                        (c.Type == ChallengeType.StaticContainer ||
                         c.Type == ChallengeType.DynamicContainer) &&
                        c.Environment == EnvironmentType.Docker &&
                        c.ImageTemplateId == null &&
                        !string.IsNullOrWhiteSpace(c.ContainerImage))
            .Select(c => c.ContainerImage!)
            .Distinct()
            .ToArrayAsync(token);

        foreach (var templateId in templateIds.OfType<int>().Distinct())
            await DistributeTemplateAsync(templateId, ImageDistributionReferenceKey.Game(gameId), token);

        foreach (var image in dockerImages)
            await DistributeDockerImageAsync(image, ImageDistributionReferenceKey.Game(gameId), token);
    }

    public async Task ReleaseGameReferencesAsync(int gameId, CancellationToken token) =>
        await ReleaseReferenceAsync(ImageDistributionReferenceKey.Game(gameId), token);

    public Task ReleaseTrainingCourseReferencesAsync(int courseId, CancellationToken token) =>
        ReleaseReferenceAsync(ImageDistributionReferenceKey.TrainingCourse(courseId), token);

    public Task ReleaseTrainingCourseTemplateReferenceAsync(
        int courseId,
        int templateId,
        CancellationToken token) =>
        ReleaseReferenceAsync(ImageDistributionReferenceKey.TrainingCourse(courseId), token, templateId);

    public Task ReleaseTeamLabRuntimeReferencesAsync(int runtimeId, CancellationToken token) =>
        ReleaseReferenceAsync(ImageDistributionReferenceKey.TeamLabRuntime(runtimeId), token);

    public async Task CleanupUnreferencedAsync(CancellationToken token)
    {
        await ReconcileReferencesAsync(token);

        var records = await context.ImageDistributionRecords
            .Include(r => r.References)
            .Where(r => !r.References.Any() ||
                        r.Status == ImageDistributionStatus.CleanupPending)
            .ToArrayAsync(token);

        foreach (var record in records)
        {
            if (record.References.Count > 0)
                continue;

            QueueCleanup(record);
        }

        await context.SaveChangesAsync(token);
        if (records.Length > 0)
            coordinator.Wake();
    }

    public async Task ReconcileReferencesAsync(CancellationToken token)
    {
        var records = await context.ImageDistributionRecords
            .Include(record => record.References)
            .Where(record => record.References.Any() ||
                             record.Status == ImageDistributionStatus.CleanupPending)
            .ToArrayAsync(token);
        if (records.Length == 0)
            return;

        var gameIds = records.SelectMany(record => record.References)
            .Where(reference => reference.Kind == ImageDistributionReferenceKind.Game)
            .Select(reference => reference.ResourceId)
            .Distinct()
            .ToArray();
        var courseIds = records.SelectMany(record => record.References)
            .Where(reference => reference.Kind == ImageDistributionReferenceKind.TrainingCourse)
            .Select(reference => reference.ResourceId)
            .Distinct()
            .ToArray();
        var runtimeIds = records.SelectMany(record => record.References)
            .Where(reference => reference.Kind == ImageDistributionReferenceKind.TeamLabRuntime)
            .Select(reference => reference.ResourceId)
            .Distinct()
            .ToArray();

        var gameReferences = (await context.GameChallenges.AsNoTracking()
                .Where(challenge => gameIds.Contains(challenge.GameId) && challenge.ImageTemplateId.HasValue)
                .Select(challenge => new { challenge.GameId, TemplateId = challenge.ImageTemplateId!.Value })
                .Distinct()
                .ToArrayAsync(token))
            .Select(reference => (reference.GameId, reference.TemplateId))
            .ToHashSet();
        var courseReferences = (await context.TrainingCourseImageTemplateBindings.AsNoTracking()
                .Where(binding => courseIds.Contains(binding.CourseId))
                .Select(binding => new { binding.CourseId, binding.ImageTemplateId })
                .ToArrayAsync(token))
            .Select(reference => (reference.CourseId, reference.ImageTemplateId))
            .ToHashSet();
        var runtimeReferences = (await context.TeamLabRuntimeAssets.AsNoTracking()
                .Where(asset => runtimeIds.Contains(asset.RuntimeId) && asset.SourceTemplateId.HasValue &&
                                asset.Runtime.Status != TeamLabRuntimeStatus.Destroyed)
                .Select(asset => new { asset.RuntimeId, TemplateId = asset.SourceTemplateId!.Value })
                .Distinct()
                .ToArrayAsync(token))
            .Select(reference => (reference.RuntimeId, reference.TemplateId))
            .ToHashSet();

        foreach (var record in records)
        {
            var invalidReferences = record.References.Where(reference => reference.Kind switch
            {
                ImageDistributionReferenceKind.Game =>
                    !gameReferences.Contains((reference.ResourceId, record.ImageTemplateId)),
                ImageDistributionReferenceKind.TrainingCourse =>
                    !courseReferences.Contains((reference.ResourceId, record.ImageTemplateId)),
                ImageDistributionReferenceKind.TeamLabRuntime =>
                    !runtimeReferences.Contains((reference.ResourceId, record.ImageTemplateId)),
                _ => true
            }).ToList();
            if (invalidReferences.Count > 0)
            {
                context.ImageDistributionReferences.RemoveRange(invalidReferences);
                AppendImageEvent(
                    record,
                    OperationalEventCodes.Image.ReconcileCorrected,
                    OperationalEventOutcome.Recovered,
                    "Image distribution references were reconciled.",
                    detail: ImageDetail(record, "stale_reference_removed"));
            }
            if (record.References.Count == invalidReferences.Count)
                QueueCleanup(record);
        }

        await context.SaveChangesAsync(token);
    }

    public async Task CleanupTemplateForDeletionAsync(int templateId, CancellationToken token)
    {
        var records = await context.ImageDistributionRecords
            .Include(record => record.WorkerNode)
            .Include(record => record.ImageTemplate)
            .Where(record => record.ImageTemplateId == templateId)
            .ToArrayAsync(token);

        foreach (var record in records)
        {
            var references = await context.ImageDistributionReferences
                .Where(reference => reference.DistributionRecordId == record.Id)
                .ToArrayAsync(token);
            context.ImageDistributionReferences.RemoveRange(references);
            QueueCleanup(record);
            await CleanupRecordAsync(record, token, removeOnSuccess: false);
        }

        await context.SaveChangesAsync(token);
        var failure = records.FirstOrDefault(record =>
            record.Status == ImageDistributionStatus.Failed ||
            !string.IsNullOrWhiteSpace(record.ErrorMessage));
        if (failure is not null)
            throw new InvalidOperationException(
                failure.ErrorMessage ??
                $"Image template {templateId} cache cleanup is incomplete on node {failure.WorkerNodeId}.");
    }

    public Task<AgentVmImageDownloadResult> EnsureVmTemplateOnNodeAsync(
        int templateId,
        Guid nodeId,
        CancellationToken token) =>
        EnsureVmTemplateOnNodeAsync(templateId, nodeId, null, token);

    public async Task<AgentVmImageDownloadResult> EnsureVmTemplateOnNodeAsync(
        int templateId,
        Guid nodeId,
        ImageDistributionReferenceKey? reference,
        CancellationToken token)
    {
        var template = await context.ImageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, token);
        if (template is null || template.Status != ImageStatus.Ready)
            return AgentVmImageDownloadResult.Failed($"VM template {templateId} was not found.");

        var node = await context.WorkerNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == nodeId, token);
        if (node is null)
            return AgentVmImageDownloadResult.Failed($"Worker node {nodeId} was not found.");

        if (!CanNodeUseImage(node, template))
            return AgentVmImageDownloadResult.Failed(
                $"Node {node.Name} cannot host VM template {template.Name} ({template.Id}).");

        var record = await QueueTemplateOnNodeAsync(template, node, reference, token);
        coordinator.Wake();
        record = await WaitForReadyAsync(record.Id, template.Name, node.Name, token);
        return record.Status == ImageDistributionStatus.Ready
            ? AgentVmImageDownloadResult.Ok(record.LastCheckedAt.HasValue, true, template.FileSize,
                $"sha256:{template.ImageHash}")
            : AgentVmImageDownloadResult.Failed(record.ErrorMessage ??
                                                $"VM template {template.Name} ({template.Id}) is not ready on node {node.Name}.");
    }

    public Task EnsureDockerImageOnNodeAsync(string image, Guid nodeId, CancellationToken token) =>
        EnsureDockerImageOnNodeAsync(image, nodeId, null, token);

    public async Task EnsureDockerImageOnNodeAsync(
        string image,
        Guid nodeId,
        ImageDistributionReferenceKey? reference,
        CancellationToken token)
    {
        var resolved = await dockerRegistry.ResolveImageReferenceAsync(image, token);
        var template = await FindReadyDockerTemplateAsync(image, resolved, token)
            ?? throw new InvalidOperationException(
                $"Docker image {resolved} is not registered as a ready platform image template.");
        var node = await context.WorkerNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == nodeId, token);
        if (node is null || !CanNodeUseImage(node, template))
            throw new InvalidOperationException($"Node {nodeId} cannot host Docker images.");

        var record = await QueueTemplateOnNodeAsync(template, node, reference, token);
        coordinator.Wake();
        record = await WaitForReadyAsync(record.Id, template.Name, node.Name, token);
        if (record.Status != ImageDistributionStatus.Ready)
            throw new InvalidOperationException(record.ErrorMessage ??
                                                $"Docker image {resolved} is not ready on node {node.Name}.");
    }

    async Task DistributeDockerImageAsync(string image, ImageDistributionReferenceKey reference, CancellationToken token)
    {
        var resolved = await dockerRegistry.ResolveImageReferenceAsync(image, token);
        var template = await FindReadyDockerTemplateAsync(image, resolved, token);
        if (template is null)
            throw new InvalidOperationException(
                $"Docker image {resolved} is not registered as a ready platform image template.");

        await DistributeTemplateAsync(template.Id, reference, token);
    }

    async Task<ImageTemplate?> FindReadyDockerTemplateAsync(
        string image,
        string resolved,
        CancellationToken token)
    {
        HashSet<string> references =
        [
            DockerImageRegistryService.NormalizeRegistryAddress(image),
            DockerImageRegistryService.NormalizeRegistryAddress(resolved)
        ];

        foreach (var reference in references.ToArray())
        {
            if (await dockerRegistry.IsManagedImageReferenceAsync(reference, token))
                references.Add(dockerRegistry.ToInternalImageReference(reference));
        }

        var normalizedReferences = references.ToArray();
        return await context.ImageTemplates.AsNoTracking()
            .Where(item => item.ImageType == ImageType.Docker &&
                           item.Status == ImageStatus.Ready &&
                           (normalizedReferences.Contains(item.RegistryUrl!) || item.Name == image))
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(token);
    }

    async Task<ImageDistributionRecord> QueueTemplateOnNodeAsync(ImageTemplate template, WorkerNode node,
        ImageDistributionReferenceKey? reference, CancellationToken token)
    {
        var hash = ResolveImageHash(template);
        await using var ownedTransaction = context.Database.CurrentTransaction is null && context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(token)
            : null;
        await AcquireDistributionLockAsync(template.Id, node.Id, token);

        var record = await context.ImageDistributionRecords
            .Include(item => item.References)
            .FirstOrDefaultAsync(r => r.ImageTemplateId == template.Id && r.WorkerNodeId == node.Id, token);
        if (record is null && IsPostgres())
        {
            var recordId = Guid.CreateVersion7();
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "ImageDistributionRecords"
                    ("Id", "ImageTemplateId", "WorkerNodeId", "ImageHash", "ImageType", "Status", "CreatedAt")
                VALUES
                    ({{recordId}}, {{template.Id}}, {{node.Id}}, {{hash}}, {{(byte)template.ImageType}},
                     {{(byte)ImageDistributionStatus.Pending}}, CURRENT_TIMESTAMP)
                ON CONFLICT ("ImageTemplateId", "WorkerNodeId") DO NOTHING
                """, token);
            record = await context.ImageDistributionRecords
                .Include(item => item.References)
                .SingleAsync(r => r.ImageTemplateId == template.Id && r.WorkerNodeId == node.Id, token);
        }
        else if (record is null)
        {
            record = new ImageDistributionRecord
            {
                ImageTemplateId = template.Id,
                WorkerNodeId = node.Id,
                ImageHash = hash,
                ImageType = template.ImageType,
                Status = ImageDistributionStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.ImageDistributionRecords.Add(record);
            await context.SaveChangesAsync(token);
        }

        var referenceAdded = await AddReferenceAsync(record, reference, token);
        if (referenceAdded)
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.ReferenceAttached,
                OperationalEventOutcome.Succeeded,
                "An image distribution reference was attached.");
        if (record.Status == ImageDistributionStatus.Ready &&
            string.Equals(record.ImageHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            record.LastCheckedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(token);
            return record;
        }

        record.ImageHash = hash;
        record.ImageType = template.ImageType;
        record.Operation = ImageDistributionOperation.Distribute;
        var queued = record.Status != ImageDistributionStatus.Pulling ||
                     record.ClaimExpiresAt <= DateTimeOffset.UtcNow;
        if (queued)
        {
            record.Status = ImageDistributionStatus.Pending;
            record.Stage = ImageDistributionStage.Queued;
            record.ClaimOwner = null;
            record.ClaimExpiresAt = null;
        }
        record.ErrorMessage = null;
        record.LastErrorCode = null;
        record.NextAttemptAt = DateTimeOffset.UtcNow;
        record.LastCheckedAt = DateTimeOffset.UtcNow;
        record.ErrorCategory = null;
        record.Retryable = false;
        record.LastCorrelationId = record.Id;
        if (queued)
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.DistributionQueued,
                OperationalEventOutcome.Pending,
                "Image distribution was queued for a worker node.");
        await context.SaveChangesAsync(token);
        if (ownedTransaction is not null)
            await ownedTransaction.CommitAsync(token);
        return record;
    }

    public async Task ProcessClaimedAsync(Guid recordId, string claimOwner, CancellationToken token)
    {
        var record = await context.ImageDistributionRecords
            .Include(item => item.ImageTemplate)
            .Include(item => item.WorkerNode)
            .Include(item => item.References)
            .SingleOrDefaultAsync(item => item.Id == recordId, token);
        if (record is null || !string.Equals(record.ClaimOwner, claimOwner, StringComparison.Ordinal))
            return;

        try
        {
            if (record.Operation == ImageDistributionOperation.Cleanup)
            {
                record.Stage = ImageDistributionStage.Cleaning;
                record.ProgressUpdatedAt = DateTimeOffset.UtcNow;
                AppendImageEvent(
                    record,
                    OperationalEventCodes.Image.CleanupStarted,
                    OperationalEventOutcome.Started,
                    "Image cache cleanup started.");
                await context.SaveChangesAsync(token);
                await CleanupRecordAsync(record, token);
                await context.SaveChangesAsync(token);
                return;
            }

            var template = record.ImageTemplate ??
                           throw new InvalidOperationException(
                               $"Image template {record.ImageTemplateId} no longer exists.");
            var node = record.WorkerNode ??
                       throw new InvalidOperationException(
                           $"Worker node {record.WorkerNodeId} no longer exists.");
            if (template.Status != ImageStatus.Ready)
                throw new InvalidOperationException(
                    $"Image template {template.Name} ({template.Id}) is not ready in storage.");
            if (!CanNodeUseImage(node, template))
                throw new InvalidOperationException(
                    $"Node {node.Name} cannot host image template {template.Name} ({template.Id}).");

            await SetTransferStageAsync(record, ImageDistributionStage.Preparing, token);
            if (template.ImageType == ImageType.Docker)
            {
                var image = await dockerRegistry.ResolveImageReferenceAsync(
                    template.RegistryUrl ?? template.Name, token);
                await SetTransferStageAsync(record, ImageDistributionStage.Pulling, token);
                await agentClient.PullDockerImageAsync(node.Id, image, template.RegistryAuth, token);
            }
            else
            {
                var artifact = await vmArtifacts.EnsureAndBuildDownloadAsync(template, node.Id, token);
                await SetTransferStageAsync(record, ImageDistributionStage.Pulling, token);
                var result = await agentClient.DownloadVmImageAsync(node.Id, template.Id, artifact.Sha256,
                    artifact.DownloadUrl, artifact.Size, token);
                if (!result.Success)
                    throw new InvalidOperationException(result.Message);
                if (!result.Verified)
                    throw new InvalidOperationException(
                        $"VM image {template.Name} ({template.Id}) was downloaded without digest verification.");
            }

            await SetTransferStageAsync(record, ImageDistributionStage.Verifying, token);
            record.Status = ImageDistributionStatus.Ready;
            record.Stage = ImageDistributionStage.None;
            record.ErrorMessage = null;
            record.LastErrorCode = null;
            record.NextAttemptAt = null;
            record.LastCheckedAt = DateTimeOffset.UtcNow;
            record.ProgressUpdatedAt = record.LastCheckedAt;
            record.ErrorCategory = null;
            record.Retryable = false;
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.TransferSucceeded,
                OperationalEventOutcome.Succeeded,
                "Image transfer completed successfully.");
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.VerifySucceeded,
                OperationalEventOutcome.Succeeded,
                "Image verification completed successfully.");
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.DistributionReady,
                OperationalEventOutcome.Succeeded,
                "Image is ready on the worker node.");
        }
        catch (Exception ex) when (IsDistributionFailure(ex, token))
        {
            var error = ImageFailure(record, ex);
            record.Status = ImageDistributionStatus.Failed;
            record.Stage = ImageDistributionStage.None;
            record.LastErrorCode = error.Code;
            record.ErrorCategory = error.Category;
            record.Retryable = error.Retryable;
            record.LastCorrelationId = record.Id;
            record.ErrorMessage = TrimError(
                $"Image template {record.ImageTemplate?.Name ?? record.ImageTemplateId.ToString()} " +
                $"on node {record.WorkerNode?.Name ?? record.WorkerNodeId.ToString()} failed: {ex.Message}");
            record.NextAttemptAt = DateTimeOffset.UtcNow.Add(RetryDelay(record.AttemptCount));
            record.ProgressUpdatedAt = DateTimeOffset.UtcNow;
            AppendImageEvent(
                record,
                record.Operation == ImageDistributionOperation.Cleanup
                    ? OperationalEventCodes.Image.CleanupFailed
                    : OperationalEventCodes.Image.DistributionFailed,
                OperationalEventOutcome.Failed,
                record.Operation == ImageDistributionOperation.Cleanup
                    ? "Image cache cleanup failed."
                    : "Image distribution failed.",
                OperationalEventSeverity.Error,
                error);
            logger.LogWarning(ex,
                "Image distribution work {RecordId} failed for template {TemplateId} on node {NodeId}.",
                record.Id, record.ImageTemplateId, record.WorkerNodeId);
        }
        finally
        {
            if (context.Entry(record).State != EntityState.Deleted)
            {
                record.ClaimOwner = null;
                record.ClaimExpiresAt = null;
                await context.SaveChangesAsync(token.IsCancellationRequested ? CancellationToken.None : token);
            }
        }
    }

    async Task<ImageDistributionRecord> WaitForReadyAsync(
        Guid recordId,
        string templateName,
        string nodeName,
        CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(2);
        ImageDistributionStage? displayedStage = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var record = await context.ImageDistributionRecords.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == recordId, token)
                ?? throw new InvalidOperationException(
                    $"Image distribution record {recordId} disappeared while preparing {templateName}.");
            if (record.Status is ImageDistributionStatus.Ready or ImageDistributionStatus.Failed)
                return record;

            if (record.Stage != displayedStage)
            {
                displayedStage = record.Stage;
                await UpdateCurrentTicketStageAsync(record.Stage, templateName, nodeName, token);
            }

            coordinator.Wake();
            await Task.Delay(TimeSpan.FromMilliseconds(500), token);
        }

        throw new TimeoutException(
            $"Timed out waiting for image template {templateName} on node {nodeName}.");
    }

    async Task SetTransferStageAsync(ImageDistributionRecord record, ImageDistributionStage stage,
        CancellationToken token)
    {
        record.Stage = stage;
        record.ProgressUpdatedAt = DateTimeOffset.UtcNow;
        record.LastCheckedAt = record.ProgressUpdatedAt;
        if (stage == ImageDistributionStage.Preparing)
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.TransferStarted,
                OperationalEventOutcome.Started,
                "Image transfer preparation started.");
        else if (stage == ImageDistributionStage.Verifying)
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.VerifyStarted,
                OperationalEventOutcome.Started,
                "Image verification started.");
        await context.SaveChangesAsync(token);
    }

    async Task UpdateCurrentTicketStageAsync(ImageDistributionStage stage, string templateName,
        string nodeName, CancellationToken token)
    {
        if (executionContext.Current?.TicketId is not { } ticketId || ticketId == Guid.Empty)
            return;
        var ticket = await context.DeploymentQueueTickets
            .SingleOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket is null || ticket.Status != DeploymentQueueTicketStatus.Running)
            return;

        (ticket.Stage, ticket.StageMessage) = stage switch
        {
            ImageDistributionStage.Preparing => (DeploymentStage.ImagePreparing,
                $"Preparing image {templateName} for node {nodeName}."),
            ImageDistributionStage.Pulling => (DeploymentStage.ImagePulling,
                $"Pulling image {templateName} to node {nodeName}."),
            ImageDistributionStage.Verifying => (DeploymentStage.ImageVerifying,
                $"Verifying image {templateName} on node {nodeName}."),
            _ => (DeploymentStage.ImagePreparing,
                $"Image {templateName} is queued for node {nodeName}.")
        };
        await context.SaveChangesAsync(token);
    }

    static TimeSpan RetryDelay(int attemptCount) =>
        TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Clamp(attemptCount - 1, 0, 6))));

    void QueueCleanup(ImageDistributionRecord record)
    {
        if (record.Status == ImageDistributionStatus.Pulling &&
            record.ClaimOwner is not null && record.ClaimExpiresAt > DateTimeOffset.UtcNow)
            return;
        var transitioned = record.Operation != ImageDistributionOperation.Cleanup ||
                           record.Status != ImageDistributionStatus.CleanupPending;
        record.Operation = ImageDistributionOperation.Cleanup;
        record.Status = ImageDistributionStatus.CleanupPending;
        record.Stage = ImageDistributionStage.Queued;
        record.ClaimOwner = null;
        record.ClaimExpiresAt = null;
        record.NextAttemptAt = DateTimeOffset.UtcNow;
        record.ErrorMessage = null;
        record.LastErrorCode = null;
        record.ErrorCategory = null;
        record.Retryable = false;
        record.LastCorrelationId = record.Id;
        if (transitioned)
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.CleanupQueued,
                OperationalEventOutcome.Pending,
                "Image cache cleanup was queued.");
    }

    async Task ReleaseReferenceAsync(
        ImageDistributionReferenceKey reference,
        CancellationToken token,
        int? templateId = null)
    {
        var referenceQuery = context.ImageDistributionReferences
            .Where(item => item.Kind == reference.Kind && item.ResourceId == reference.ResourceId);
        if (templateId.HasValue)
            referenceQuery = referenceQuery.Where(item => item.DistributionRecord.ImageTemplateId == templateId.Value);

        var candidates = await referenceQuery
            .Select(item => new
            {
                item.DistributionRecordId,
                item.DistributionRecord.ImageTemplateId,
                item.DistributionRecord.WorkerNodeId
            })
            .Distinct()
            .OrderBy(item => item.ImageTemplateId)
            .ThenBy(item => item.WorkerNodeId)
            .ToArrayAsync(token);
        if (candidates.Length == 0)
            return;

        foreach (var candidate in candidates)
        {
            await using var transaction = context.Database.CurrentTransaction is null && context.Database.IsRelational()
                ? await context.Database.BeginTransactionAsync(token)
                : null;
            await AcquireDistributionLockAsync(candidate.ImageTemplateId, candidate.WorkerNodeId, token);

            var currentReference = await context.ImageDistributionReferences.SingleOrDefaultAsync(item =>
                item.DistributionRecordId == candidate.DistributionRecordId &&
                item.Kind == reference.Kind && item.ResourceId == reference.ResourceId, token);
            if (currentReference is null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(token);
                continue;
            }

            var record = await context.ImageDistributionRecords
                .Include(item => item.WorkerNode)
                .Include(item => item.ImageTemplate)
                .SingleOrDefaultAsync(item => item.Id == candidate.DistributionRecordId, token);
            if (record is null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(token);
                continue;
            }

            context.ImageDistributionReferences.Remove(currentReference);
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.ReferenceReleased,
                OperationalEventOutcome.Succeeded,
                "An image distribution reference was released.");
            await context.SaveChangesAsync(token);
            if (await context.ImageDistributionReferences.AnyAsync(
                    item => item.DistributionRecordId == candidate.DistributionRecordId, token))
            {
                if (transaction is not null)
                    await transaction.CommitAsync(token);
                continue;
            }

            QueueCleanup(record);
            await context.SaveChangesAsync(token);
            if (transaction is not null)
                await transaction.CommitAsync(token);
            coordinator.Wake();
        }
    }

    async Task CleanupRecordAsync(
        ImageDistributionRecord record,
        CancellationToken token,
        bool removeOnSuccess = true)
    {
        try
        {
            if (record.ImageType == ImageType.Docker)
            {
                var image = record.ImageTemplate?.RegistryUrl ?? record.ImageTemplate?.Name;
                if (!string.IsNullOrWhiteSpace(image))
                    await agentClient.DeleteDockerImageAsync(record.WorkerNodeId,
                        dockerRegistry.ResolveInternalImageReferenceForConfiguredRegistry(image), token);
            }
            else
            {
                if (await HasActiveVmUsingTemplateAsync(record, token))
                {
                    record.Status = ImageDistributionStatus.CleanupPending;
                    record.ErrorMessage = "VM image cache is still referenced by an active VM on this node.";
                    record.LastCheckedAt = DateTimeOffset.UtcNow;
                    AppendImageEvent(
                        record,
                        OperationalEventCodes.Image.CleanupQueued,
                        OperationalEventOutcome.Blocked,
                        "Image cache cleanup is waiting for active VM references.",
                        OperationalEventSeverity.Warning,
                        detail: ImageDetail(record, "active_vm_reference"));
                    return;
                }

                await agentClient.DeleteVmImageAsync(record.WorkerNodeId, record.ImageTemplateId,
                    record.ImageHash, token);
            }

            if (removeOnSuccess)
            {
                AppendImageEvent(
                    record,
                    OperationalEventCodes.Image.CleanupSucceeded,
                    OperationalEventOutcome.Succeeded,
                    "Image cache cleanup completed successfully.");
                context.ImageDistributionRecords.Remove(record);
            }
            else
            {
                record.Status = ImageDistributionStatus.CleanupPending;
                record.ErrorMessage = null;
                record.LastCheckedAt = DateTimeOffset.UtcNow;
                AppendImageEvent(
                    record,
                    OperationalEventCodes.Image.CleanupSucceeded,
                    OperationalEventOutcome.Succeeded,
                    "Image cache cleanup completed successfully.");
            }
        }
        catch (Exception ex) when (IsDistributionFailure(ex, token))
        {
            var error = ImageFailure(record, ex);
            record.Status = ImageDistributionStatus.Failed;
            record.Stage = ImageDistributionStage.None;
            record.LastErrorCode = error.Code;
            record.ErrorCategory = error.Category;
            record.Retryable = error.Retryable;
            record.LastCorrelationId = record.Id;
            record.ErrorMessage = TrimError($"Image cache cleanup failed: {ex.Message}");
            record.NextAttemptAt = DateTimeOffset.UtcNow.Add(RetryDelay(record.AttemptCount));
            record.ProgressUpdatedAt = DateTimeOffset.UtcNow;
            AppendImageEvent(
                record,
                OperationalEventCodes.Image.CleanupFailed,
                OperationalEventOutcome.Failed,
                "Image cache cleanup failed.",
                OperationalEventSeverity.Error,
                error);
            logger.LogWarning(ex,
                "Failed to cleanup image template {TemplateId} on node {NodeId}.",
                record.ImageTemplateId, record.WorkerNodeId);
        }
    }

    async Task<bool> HasActiveVmUsingTemplateAsync(ImageDistributionRecord record, CancellationToken token) =>
        await context.VmInstances.AsNoTracking()
            .Where(vm => vm.NodeId == record.WorkerNodeId &&
                         (vm.Status == VmInstanceStatus.Creating ||
                          vm.Status == VmInstanceStatus.Running))
            .Join(context.GameChallenges.AsNoTracking(),
                vm => vm.ChallengeId,
                challenge => challenge.Id,
                (_, challenge) => challenge.ImageTemplateId)
            .AnyAsync(templateId => templateId == record.ImageTemplateId, token) ||
        await context.TeamLabRuntimeAssets.AsNoTracking().AnyAsync(asset =>
            asset.WorkerNodeId == record.WorkerNodeId &&
            asset.SourceTemplateId == record.ImageTemplateId &&
            asset.Kind == TeamLabResourceKind.Vm &&
            asset.Runtime.Status != TeamLabRuntimeStatus.Destroyed &&
            asset.Status != TeamLabRuntimeStatus.Destroyed, token);

    async Task<WorkerNode[]> GetCapableNodesAsync(ImageTemplate template, CancellationToken token)
    {
        var capability = template.ImageType == ImageType.Docker ? DockerCapability : VmCapability;
        return await context.WorkerNodes.AsNoTracking()
            .Where(n => n.Status == NodeStatus.Online &&
                        n.IsSchedulable &&
                        (n.Capabilities & capability) == capability)
            .OrderBy(n => n.Name)
            .ThenBy(n => n.Id)
            .ToArrayAsync(token);
    }

    static bool CanNodeUseImage(WorkerNode node, ImageTemplate template)
    {
        var capability = template.ImageType == ImageType.Docker ? DockerCapability : VmCapability;
        return node.Status == NodeStatus.Online &&
               node.IsSchedulable &&
               (node.Capabilities & capability) == capability;
    }

    static string ResolveImageHash(ImageTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(template.ImageHash))
            return template.ImageHash.Trim();

        if (template.ImageType == ImageType.Docker && !string.IsNullOrWhiteSpace(template.RegistryUrl))
            return template.RegistryUrl.Trim();

        throw new InvalidOperationException($"Image template {template.Name} ({template.Id}) has no image hash.");
    }

    async Task<bool> AddReferenceAsync(
        ImageDistributionRecord record,
        ImageDistributionReferenceKey? reference,
        CancellationToken token)
    {
        if (reference is not { } key)
            return false;

        if (context.Database.IsRelational())
        {
            var affected = await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "ImageDistributionReferences"
                    ("Id", "DistributionRecordId", "Kind", "ResourceId", "CreatedAt")
                VALUES
                    ({{Guid.CreateVersion7()}}, {{record.Id}}, {{(byte)key.Kind}}, {{key.ResourceId}}, CURRENT_TIMESTAMP)
                ON CONFLICT ("DistributionRecordId", "Kind", "ResourceId") DO NOTHING
                """, token);
            return affected > 0;
        }

        if (await context.ImageDistributionReferences.AnyAsync(item =>
                item.DistributionRecordId == record.Id &&
                item.Kind == key.Kind &&
                item.ResourceId == key.ResourceId, token))
            return false;

        var entity = new ImageDistributionReference
        {
            DistributionRecordId = record.Id,
            Kind = key.Kind,
            ResourceId = key.ResourceId
        };
        record.References.Add(entity);
        context.ImageDistributionReferences.Add(entity);
        return true;
    }

    async Task AcquireDistributionLockAsync(int templateId, Guid nodeId, CancellationToken token)
    {
        if (!IsPostgres())
            return;

        var lockKey = $"image-distribution:{templateId}:{nodeId:N}";
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", token);
    }

    bool IsPostgres() =>
        context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;

    void AppendImageEvent(
        ImageDistributionRecord record,
        string eventCode,
        OperationalEventOutcome outcome,
        string message,
        OperationalEventSeverity severity = OperationalEventSeverity.Information,
        OperationalError? error = null,
        IReadOnlyDictionary<string, object?>? detail = null) =>
        events.Append(BuildOperationalEvent(record, eventCode, outcome, message, severity, error, detail));

    internal static OperationalEventDraft BuildOperationalEvent(
        ImageDistributionRecord record,
        string eventCode,
        OperationalEventOutcome outcome,
        string message,
        OperationalEventSeverity severity = OperationalEventSeverity.Information,
        OperationalError? error = null,
        IReadOnlyDictionary<string, object?>? detail = null) =>
        new(
            eventCode,
            outcome,
            message,
            severity,
            record.Id,
            error?.Category,
            error?.Code,
            error?.Retryable ?? false,
            detail ?? ImageDetail(record),
            ImageTemplateId: record.ImageTemplateId,
            WorkerNodeId: record.WorkerNodeId,
            SubjectType: "image-distribution",
            SubjectId: record.Id.ToString(),
            SubjectDisplayName: record.ImageTemplate?.Name,
            ResourceType: "image-template",
            ResourceId: record.ImageTemplateId.ToString(),
            ResourceDisplayName: record.ImageTemplate?.Name);

    static OperationalError ImageFailure(ImageDistributionRecord record, Exception exception)
    {
        var operation = record.Operation == ImageDistributionOperation.Cleanup
            ? "image.cleanup"
            : "image.distribute";
        if (exception is AgentClientException agent)
            return agent.Error with { WorkerNodeId = record.WorkerNodeId, Operation = operation };
        return new OperationalError(
            record.Operation == ImageDistributionOperation.Cleanup
                ? OperationalErrorCategory.Storage
                : OperationalErrorCategory.ImageTransfer,
            record.Operation == ImageDistributionOperation.Cleanup
                ? OperationalErrorCodes.ImageCleanupFailed
                : OperationalErrorCodes.ImageTransferFailed,
            "Image distribution operation failed.",
            exception is HttpRequestException or IOException or TimeoutException,
            WorkerNodeId: record.WorkerNodeId,
            Operation: operation);
    }

    static IReadOnlyDictionary<string, object?> ImageDetail(
        ImageDistributionRecord record,
        string? reasonCode = null) =>
        new Dictionary<string, object?>
        {
            ["imageType"] = record.ImageType.ToString(),
            ["operation"] = record.Operation.ToString(),
            ["stage"] = record.Stage.ToString(),
            ["attempt"] = record.AttemptCount,
            ["reasonCode"] = reasonCode
        };

    static string TrimError(string message) =>
        message.Length <= 1024 ? message : message[..1024];

    static bool IsDistributionFailure(Exception exception, CancellationToken callerToken) =>
        exception switch
        {
            OperationCanceledException => !callerToken.IsCancellationRequested,
            HttpRequestException or IOException or InvalidOperationException or AgentClientException => true,
            _ => false
        };
}
