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

    public async Task DistributeToCapableNodesAsync(ImageTemplate template, CancellationToken token) =>
        await DistributeTemplateAsync(template.Id, null, token);

    public async Task DistributeTemplateAsync(int templateId, ImageDistributionReference? reference,
        CancellationToken token)
    {
        var template = await context.ImageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, token);
        if (template is null)
            return;

        var nodes = await GetCapableNodesAsync(template, token);
        foreach (var node in nodes)
            await EnsureTemplateOnNodeAsync(template, node, reference, token);
    }

    public async Task DistributeGameAsync(int gameId, CancellationToken token)
    {
        var templateIds = await context.GameChallenges.AsNoTracking()
            .Where(c => c.GameId == gameId && c.Type.IsContainer())
            .Select(c => c.Environment == EnvironmentType.WindowsVM ? c.ImageTemplateId : null)
            .ToArrayAsync(token);

        var dockerImages = await context.GameChallenges.AsNoTracking()
            .Where(c => c.GameId == gameId &&
                        c.Type.IsContainer() &&
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

    public async Task CleanupUnreferencedAsync(CancellationToken token)
    {
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

    public async Task<AgentVmImageDownloadResult> EnsureVmTemplateOnNodeAsync(int templateId, Guid nodeId,
        CancellationToken token)
    {
        var template = await context.ImageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, token);
        if (template is null)
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
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
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

    async Task ReleaseReferenceAsync(ImageDistributionReference reference, CancellationToken token)
    {
        var records = (await context.ImageDistributionRecords
            .Include(r => r.WorkerNode)
            .Include(r => r.ImageTemplate)
            .ToArrayAsync(token))
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

    async Task CleanupRecordAsync(ImageDistributionRecord record, CancellationToken token)
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

            context.ImageDistributionRecords.Remove(record);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
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
}
