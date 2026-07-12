using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class ImageDistributionService(
    AppDbContext context,
    AgentClient agentClient,
    DockerImageRegistryService dockerRegistry,
    VmArtifactStore vmArtifacts,
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

        persisted.Status = ImageStatus.Importing;
        persisted.ErrorMessage = null;
        await context.SaveChangesAsync(token);

        var records = await DistributeTemplateAsync(template.Id, reference, token);
        if (records.Count == 0)
        {
            var imageKind = template.ImageType == ImageType.Docker ? "Docker" : "KVM";
            var message = $"No online schedulable {imageKind} node is available for image template " +
                          $"{template.Name} ({template.Id}).";
            persisted.Status = ImageStatus.Error;
            persisted.ErrorMessage = TrimError(message);
            await context.SaveChangesAsync(token);
            throw new InvalidOperationException(message);
        }

        var failures = records
            .Where(record => record.Status == ImageDistributionStatus.Failed)
            .Select(record => record.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        persisted.Status = failures.Length == 0 ? ImageStatus.Ready : ImageStatus.Error;
        persisted.ErrorMessage = failures.Length == 0
            ? null
            : TrimError(string.Join("; ", failures));
        await context.SaveChangesAsync(token);
        if (failures.Length > 0)
            throw new InvalidOperationException(persisted.ErrorMessage);

        return records;
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
            records.Add(await EnsureTemplateOnNodeAsync(template, node, reference, token));
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

    public async Task CleanupUnreferencedAsync(CancellationToken token)
    {
        await ReconcileReferencesAsync(token);

        var records = await context.ImageDistributionRecords
            .Include(r => r.WorkerNode)
            .Include(r => r.ImageTemplate)
            .Include(r => r.References)
            .Where(r => !r.References.Any() ||
                        r.Status == ImageDistributionStatus.CleanupPending)
            .ToArrayAsync(token);

        foreach (var record in records)
        {
            if (record.References.Count > 0)
                continue;

            record.Status = ImageDistributionStatus.CleanupPending;
            await CleanupRecordAsync(record, token);
        }

        await context.SaveChangesAsync(token);
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

        foreach (var record in records)
        {
            var invalidReferences = record.References.Where(reference => reference.Kind switch
            {
                ImageDistributionReferenceKind.Game =>
                    !gameReferences.Contains((reference.ResourceId, record.ImageTemplateId)),
                ImageDistributionReferenceKind.TrainingCourse =>
                    !courseReferences.Contains((reference.ResourceId, record.ImageTemplateId)),
                _ => true
            }).ToList();
            if (invalidReferences.Count > 0)
                context.ImageDistributionReferences.RemoveRange(invalidReferences);
            if (record.References.Count == invalidReferences.Count)
                record.Status = ImageDistributionStatus.CleanupPending;
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
            record.Status = ImageDistributionStatus.CleanupPending;
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

    public async Task<AgentVmImageDownloadResult> EnsureVmTemplateOnNodeAsync(int templateId, Guid nodeId,
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

        var record = await EnsureTemplateOnNodeAsync(template, node, null, token);
        return record.Status == ImageDistributionStatus.Ready
            ? AgentVmImageDownloadResult.Ok(record.LastCheckedAt.HasValue, true, template.FileSize,
                $"sha256:{template.ImageHash}")
            : AgentVmImageDownloadResult.Failed(record.ErrorMessage ??
                                                $"VM template {template.Name} ({template.Id}) is not ready on node {node.Name}.");
    }

    public async Task EnsureDockerImageOnNodeAsync(string image, Guid nodeId, CancellationToken token)
    {
        var node = await context.WorkerNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == nodeId, token);
        if (node is null || (node.Capabilities & NodeCapability.Docker) != NodeCapability.Docker)
            throw new InvalidOperationException($"Node {nodeId} cannot host Docker images.");

        var resolved = await dockerRegistry.ResolveImageReferenceAsync(image, token);
        await agentClient.PullDockerImageAsync(nodeId, resolved, null, token);
    }

    async Task DistributeDockerImageAsync(string image, ImageDistributionReferenceKey reference, CancellationToken token)
    {
        var resolved = await dockerRegistry.ResolveImageReferenceAsync(image, token);
        var template = await context.ImageTemplates.AsNoTracking()
            .Where(item => item.ImageType == ImageType.Docker &&
                           item.Status == ImageStatus.Ready &&
                           item.RegistryUrl == resolved)
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(token);
        if (template is null)
            throw new InvalidOperationException(
                $"Docker image {resolved} is not registered as a ready platform image template.");

        var records = await DistributeTemplateAsync(template.Id, reference, token);
        var failed = records.FirstOrDefault(record => record.Status == ImageDistributionStatus.Failed);
        if (failed is not null)
            throw new InvalidOperationException(failed.ErrorMessage ??
                                                $"Docker image {resolved} could not be distributed.");
    }

    async Task<ImageDistributionRecord> EnsureTemplateOnNodeAsync(ImageTemplate template, WorkerNode node,
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

        await AddReferenceAsync(record, reference, token);
        await context.SaveChangesAsync(token);
        if (ownedTransaction is not null)
            await ownedTransaction.CommitAsync(token);

        if (record.Status == ImageDistributionStatus.Ready &&
            string.Equals(record.ImageHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            record.LastCheckedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);
            return record;
        }

        record.ImageHash = hash;
        record.ImageType = template.ImageType;
        record.Status = ImageDistributionStatus.Pulling;
        record.ErrorMessage = null;
        record.LastCheckedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        try
        {
            if (template.ImageType == ImageType.Docker)
            {
                var image = await dockerRegistry.ResolveImageReferenceAsync(template.RegistryUrl ?? template.Name, token);
                await agentClient.PullDockerImageAsync(node.Id, image, template.RegistryAuth, token);
            }
            else
            {
                var artifact = await vmArtifacts.EnsureAndBuildDownloadAsync(template, node.Id, token);
                var result = await agentClient.DownloadVmImageAsync(node.Id, template.Id, artifact.Sha256,
                    artifact.DownloadUrl, artifact.Size, token);
                if (!result.Success)
                    throw new InvalidOperationException(result.Message);
            }

            record.Status = ImageDistributionStatus.Ready;
            record.ErrorMessage = null;
            record.LastCheckedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (IsDistributionFailure(ex, token))
        {
            record.Status = ImageDistributionStatus.Failed;
            record.ErrorMessage = TrimError(
                $"Failed to distribute image template {template.Name} ({template.Id}) to node {node.Name}: {ex.Message}");
            logger.LogWarning(ex,
                "Failed to distribute image template {TemplateId} to node {NodeName} ({NodeId}).",
                template.Id, node.Name, node.Id);
        }

        await context.SaveChangesAsync(token);
        return record;
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

            context.ImageDistributionReferences.Remove(currentReference);
            await context.SaveChangesAsync(token);
            if (await context.ImageDistributionReferences.AnyAsync(
                    item => item.DistributionRecordId == candidate.DistributionRecordId, token))
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

            record.Status = ImageDistributionStatus.CleanupPending;
            await CleanupRecordAsync(record, token);
            await context.SaveChangesAsync(token);
            if (transaction is not null)
                await transaction.CommitAsync(token);
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
                    return;
                }

                await agentClient.DeleteVmImageAsync(record.WorkerNodeId, record.ImageTemplateId,
                    record.ImageHash, token);
            }

            if (removeOnSuccess)
                context.ImageDistributionRecords.Remove(record);
            else
            {
                record.Status = ImageDistributionStatus.CleanupPending;
                record.ErrorMessage = null;
                record.LastCheckedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex) when (IsDistributionFailure(ex, token))
        {
            record.Status = ImageDistributionStatus.Failed;
            record.ErrorMessage = TrimError($"Image cache cleanup failed: {ex.Message}");
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
            .AnyAsync(templateId => templateId == record.ImageTemplateId, token);

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

    async Task AddReferenceAsync(
        ImageDistributionRecord record,
        ImageDistributionReferenceKey? reference,
        CancellationToken token)
    {
        if (reference is not { } key)
            return;

        if (context.Database.IsRelational())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "ImageDistributionReferences"
                    ("Id", "DistributionRecordId", "Kind", "ResourceId", "CreatedAt")
                VALUES
                    ({{Guid.CreateVersion7()}}, {{record.Id}}, {{(byte)key.Kind}}, {{key.ResourceId}}, CURRENT_TIMESTAMP)
                ON CONFLICT ("DistributionRecordId", "Kind", "ResourceId") DO NOTHING
                """, token);
            return;
        }

        if (await context.ImageDistributionReferences.AnyAsync(item =>
                item.DistributionRecordId == record.Id &&
                item.Kind == key.Kind &&
                item.ResourceId == key.ResourceId, token))
            return;

        var entity = new ImageDistributionReference
        {
            DistributionRecordId = record.Id,
            Kind = key.Kind,
            ResourceId = key.ResourceId
        };
        record.References.Add(entity);
        context.ImageDistributionReferences.Add(entity);
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
