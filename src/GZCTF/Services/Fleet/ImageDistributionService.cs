using GZCTF.Models.Data;
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
        ImageDistributionReference? reference = null)
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
        ImageDistributionReference? reference,
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
            .Select(c => c.Environment == EnvironmentType.WindowsVM ? c.ImageTemplateId : null)
            .ToArrayAsync(token);

        var dockerImages = await context.GameChallenges.AsNoTracking()
            .Where(c => c.GameId == gameId &&
                        (c.Type == ChallengeType.StaticContainer ||
                         c.Type == ChallengeType.DynamicContainer) &&
                        c.Environment == EnvironmentType.Docker &&
                        !string.IsNullOrWhiteSpace(c.ContainerImage))
            .Select(c => c.ContainerImage!)
            .Distinct()
            .ToArrayAsync(token);

        foreach (var templateId in templateIds.OfType<int>().Distinct())
            await DistributeTemplateAsync(templateId, ImageDistributionReference.Game(gameId), token);

        foreach (var image in dockerImages)
            await DistributeDockerImageAsync(image, ImageDistributionReference.Game(gameId), token);
    }

    public async Task ReleaseGameReferencesAsync(int gameId, CancellationToken token) =>
        await ReleaseReferenceAsync(ImageDistributionReference.Game(gameId), token);

    public Task ReleaseTrainingCourseReferencesAsync(int courseId, CancellationToken token) =>
        ReleaseReferenceAsync(ImageDistributionReference.TrainingCourse(courseId), token);

    public Task ReleaseTrainingCourseTemplateReferenceAsync(
        int courseId,
        int templateId,
        CancellationToken token) =>
        ReleaseReferenceAsync(ImageDistributionReference.TrainingCourse(courseId), token, templateId);

    public async Task CleanupUnreferencedAsync(CancellationToken token)
    {
        await ReconcileReferencesAsync(token);

        var records = await context.ImageDistributionRecords
            .Include(r => r.WorkerNode)
            .Include(r => r.ImageTemplate)
            .Where(r => r.ReferenceCount <= 0 ||
                        r.Status == ImageDistributionStatus.CleanupPending)
            .ToArrayAsync(token);

        foreach (var record in records)
        {
            record.ReferenceCount = Math.Max(0, record.References.Count);
            if (record.ReferenceCount > 0)
                continue;

            record.Status = ImageDistributionStatus.CleanupPending;
            await CleanupRecordAsync(record, token);
        }

        await context.SaveChangesAsync(token);
    }

    public async Task ReconcileReferencesAsync(CancellationToken token)
    {
        var records = (await context.ImageDistributionRecords
                .Where(record => record.ReferenceCount > 0 ||
                                 record.Status == ImageDistributionStatus.CleanupPending)
                .ToArrayAsync(token))
            .Where(record => record.References.Count > 0)
            .ToArray();
        if (records.Length == 0)
            return;

        var gameIds = records.SelectMany(record => record.References)
            .Where(reference => reference.Kind == ImageDistributionReferenceKind.Game)
            .Select(reference => reference.Id)
            .Distinct()
            .ToArray();
        var courseIds = records.SelectMany(record => record.References)
            .Where(reference => reference.Kind == ImageDistributionReferenceKind.TrainingCourse)
            .Select(reference => reference.Id)
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
            var validReferences = record.References.Where(reference => reference.Kind switch
            {
                ImageDistributionReferenceKind.Game =>
                    gameReferences.Contains((reference.Id, record.ImageTemplateId)),
                ImageDistributionReferenceKind.TrainingCourse =>
                    courseReferences.Contains((reference.Id, record.ImageTemplateId)),
                _ => false
            }).ToList();
            if (validReferences.Count == record.References.Count)
            {
                record.ReferenceCount = Math.Max(record.ReferenceCount, validReferences.Count);
                continue;
            }

            record.References = validReferences;
            record.ReferenceCount = validReferences.Count;
            if (record.ReferenceCount == 0)
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
            record.ReferenceCount = 0;
            record.References = [];
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

    async Task DistributeDockerImageAsync(string image, ImageDistributionReference reference, CancellationToken token)
    {
        var resolved = await dockerRegistry.ResolveImageReferenceAsync(image, token);
        var pseudoTemplate = new ImageTemplate
        {
            Id = 0,
            Name = resolved,
            ImageType = ImageType.Docker,
            RegistryUrl = resolved,
            ImageHash = resolved,
            Status = ImageStatus.Ready
        };
        var nodes = await GetCapableNodesAsync(pseudoTemplate, token);
        foreach (var node in nodes)
            await agentClient.PullDockerImageAsync(node.Id, resolved, null, token);
    }

    async Task<ImageDistributionRecord> EnsureTemplateOnNodeAsync(ImageTemplate template, WorkerNode node,
        ImageDistributionReference? reference, CancellationToken token)
    {
        var hash = ResolveImageHash(template);
        var record = await context.ImageDistributionRecords
            .FirstOrDefaultAsync(r => r.ImageTemplateId == template.Id && r.WorkerNodeId == node.Id, token);

        if (record is null)
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
        }

        AddReference(record, reference);
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
        ImageDistributionReference reference,
        CancellationToken token,
        int? templateId = null)
    {
        var query = context.ImageDistributionRecords
            .Include(r => r.WorkerNode)
            .Include(r => r.ImageTemplate)
            .AsQueryable();
        if (templateId.HasValue)
            query = query.Where(record => record.ImageTemplateId == templateId.Value);

        var records = (await query.ToArrayAsync(token))
            .Where(r => r.References.Contains(reference))
            .ToArray();

        foreach (var record in records)
        {
            var before = record.References.Count;
            record.References = record.References.Where(r => r != reference).ToList();
            if (record.References.Count != before)
                record.ReferenceCount = Math.Max(record.References.Count, record.ReferenceCount - 1);
            else
                record.ReferenceCount = Math.Max(record.ReferenceCount, record.References.Count);
            if (record.ReferenceCount > 0)
                continue;

            record.Status = ImageDistributionStatus.CleanupPending;
            await CleanupRecordAsync(record, token);
        }

        await context.SaveChangesAsync(token);
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

    static void AddReference(ImageDistributionRecord record, ImageDistributionReference? reference)
    {
        if (reference is not null && !record.References.Contains(reference))
        {
            record.References.Add(reference);
            record.ReferenceCount = Math.Max(record.References.Count, record.ReferenceCount + 1);
            return;
        }

        record.ReferenceCount = Math.Max(record.ReferenceCount, record.References.Count);
        if (reference is null && record.ReferenceCount == 0)
            record.ReferenceCount = 1;
    }

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
