using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class DockerRegistryMigrationService(
    AppDbContext context,
    DockerImageRegistryService registry,
    IDistributedLockService lockService,
    ILogger<DockerRegistryMigrationService> logger)
{
    static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);

    public async Task<DockerRegistryMigrationTask> CreateTaskAsync(Guid targetNodeId, CancellationToken token)
    {
        using var migrationLock = await lockService.AcquireAsync("docker-registry:migration", LockTimeout);
        var targetNode = await context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == targetNodeId, token)
                         ?? throw new InvalidOperationException("目标节点不存在。");

        var targetRegistry = BuildRegistryAddress(targetNode);
        var sourceRegistry = (await registry.GetActiveEndpointAsync(token))?.Address ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetRegistry))
            throw new InvalidOperationException("目标节点地址无效，无法作为镜像仓库。");

        var runningTask = await context.DockerRegistryMigrationTasks
            .Where(t => t.Status == DockerRegistryMigrationStatus.Pending ||
                        t.Status == DockerRegistryMigrationStatus.Running)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(token);
        if (runningTask is not null)
            return runningTask;

        var allTemplates = await context.ImageTemplates.AsNoTracking()
            .Where(t => t.ImageType == ImageType.Docker &&
                        !string.IsNullOrWhiteSpace(t.RegistryUrl) &&
                        t.Status != ImageStatus.Error)
            .OrderBy(t => t.Id)
            .ToArrayAsync(token);
        var templates = new List<ImageTemplate>();
        foreach (var template in allTemplates)
        {
            if (await registry.IsManagedImageReferenceAsync(template.RegistryUrl, token))
                templates.Add(template);
        }

        var task = new DockerRegistryMigrationTask
        {
            TargetNodeId = targetNode.Id,
            SourceRegistry = sourceRegistry,
            TargetRegistry = targetRegistry,
            Status = DockerRegistryMigrationStatus.Pending,
            TotalItems = templates.Count,
            Message = templates.Count == 0 ? "没有需要同步的 Docker 镜像。" : "等待同步 Docker 镜像。"
        };

        foreach (var template in templates)
        {
            var sourceImage = template.RegistryUrl!;
            task.Items.Add(new DockerRegistryMigrationItem
            {
                ImageTemplateId = template.Id,
                SourceImage = sourceImage,
                TargetImage = registry.BuildImageReferenceForRegistryFromReference(targetRegistry, sourceImage)
            });
        }

        context.DockerRegistryMigrationTasks.Add(task);
        await context.SaveChangesAsync(token);
        return task;
    }

    public async Task<DockerRegistryMigrationTask?> GetLatestTaskAsync(CancellationToken token) =>
        await context.DockerRegistryMigrationTasks.AsNoTracking()
            .Include(t => t.TargetNode)
            .Include(t => t.Items.OrderBy(i => i.CreatedAt))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(token);

    public async Task RunTaskAsync(Guid taskId, CancellationToken token)
    {
        using var migrationLock = await lockService.AcquireAsync("docker-registry:migration", TimeSpan.FromSeconds(30));
        var task = await context.DockerRegistryMigrationTasks
            .Include(t => t.Items.OrderBy(i => i.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == taskId, token);
        if (task is null || task.Status is DockerRegistryMigrationStatus.Completed or DockerRegistryMigrationStatus.Cancelled)
            return;

        task.Status = DockerRegistryMigrationStatus.Running;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        task.Message = "正在准备目标镜像仓库。";
        await context.SaveChangesAsync(token);

        var targetNode = await context.WorkerNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == task.TargetNodeId, token)
            ?? throw new InvalidOperationException("目标节点不存在。");
        task.TargetRegistry = BuildRegistryAddress(targetNode);
        if (string.IsNullOrWhiteSpace(task.TargetRegistry))
            throw new InvalidOperationException("目标节点地址无效，无法作为镜像仓库。");

        await registry.EnsureNodeRegistryAsync(task.TargetNodeId, token);
        await registry.ConfigureFleetRegistryTrustAsync(task.TargetRegistry, token);

        task.Message = "正在同步 Docker 镜像到新的存储节点。";
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        foreach (var item in task.Items.Where(i => i.Status != DockerRegistryMigrationStatus.Completed))
        {
            try
            {
                token.ThrowIfCancellationRequested();
                item.Status = DockerRegistryMigrationStatus.Running;
                item.RetryCount++;
                item.ErrorMessage = null;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(token);

                var template = await context.ImageTemplates.FirstOrDefaultAsync(t => t.Id == item.ImageTemplateId, token);
                if (template is null || string.IsNullOrWhiteSpace(template.RegistryUrl))
                {
                    item.Status = DockerRegistryMigrationStatus.Completed;
                    item.CompletedAt = DateTimeOffset.UtcNow;
                    item.UpdatedAt = DateTimeOffset.UtcNow;
                    await context.SaveChangesAsync(token);
                    continue;
                }

                item.SourceImage = await registry.ResolveImageReferenceAsync(template.RegistryUrl, token);
                item.TargetImage = registry.BuildImageReferenceForRegistryFromReference(task.TargetRegistry, template.RegistryUrl);
                await context.SaveChangesAsync(token);

                await MigrateItemAsync(item, token);

                template.RegistryUrl = await registry.IsManagedImageReferenceAsync(template.RegistryUrl, token)
                    ? registry.ToInternalImageReference(template.RegistryUrl)
                    : template.RegistryUrl;
                template.Status = ImageStatus.Ready;
                template.UploadedAt = DateTimeOffset.UtcNow;

                item.Status = DockerRegistryMigrationStatus.Completed;
                item.CompletedAt = DateTimeOffset.UtcNow;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                task.CompletedItems = task.Items.Count(i => i.Status == DockerRegistryMigrationStatus.Completed);
                task.FailedItems = task.Items.Count(i => i.Status == DockerRegistryMigrationStatus.Failed);
                task.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to migrate Docker image {SourceImage} to {TargetImage}.",
                    item.SourceImage, item.TargetImage);
                item.Status = DockerRegistryMigrationStatus.Failed;
                item.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                task.FailedItems = task.Items.Count(i => i.Status == DockerRegistryMigrationStatus.Failed);
                task.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(CancellationToken.None);
            }
        }

        task.CompletedItems = task.Items.Count(i => i.Status == DockerRegistryMigrationStatus.Completed);
        task.FailedItems = task.Items.Count(i => i.Status == DockerRegistryMigrationStatus.Failed);
        task.Status = task.FailedItems == 0
            ? DockerRegistryMigrationStatus.Completed
            : DockerRegistryMigrationStatus.Failed;
        task.CompletedAt = DateTimeOffset.UtcNow;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        task.Message = task.Status == DockerRegistryMigrationStatus.Completed
            ? "镜像已全部同步并切换到新的存储节点。"
            : $"镜像同步完成，但有 {task.FailedItems} 个镜像失败；失败镜像仍保留旧地址。";

        if (task.Status == DockerRegistryMigrationStatus.Completed)
        {
            await context.WorkerNodes.ExecuteUpdateAsync(s => s.SetProperty(n => n.IsStorageNode, false),
                CancellationToken.None);
            await context.WorkerNodes
                .Where(n => n.Id == task.TargetNodeId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsStorageNode, true), CancellationToken.None);

            try
            {
                await registry.ConfigureManagedRegistryTrustAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Docker registry migration completed, but fleet registry trust refresh failed.");
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    async Task MigrateItemAsync(DockerRegistryMigrationItem item, CancellationToken token)
    {
        await registry.RunDockerCommandAsync(["pull", item.SourceImage], token);
        item.SourceDigest = await TryInspectDigest(item.SourceImage, token);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        await registry.RunDockerCommandAsync(["tag", item.SourceImage, item.TargetImage], token);
        await registry.RunDockerCommandAsync(["push", item.TargetImage], token);
        item.TargetDigest = await TryInspectDigest(item.TargetImage, token);
        item.UpdatedAt = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(item.SourceDigest) &&
            !string.IsNullOrWhiteSpace(item.TargetDigest) &&
            !string.Equals(DigestHash(item.SourceDigest), DigestHash(item.TargetDigest), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目标镜像 digest 与源镜像不一致，已拒绝切换模板地址。");
    }

    async Task<string?> TryInspectDigest(string image, CancellationToken token)
    {
        try
        {
            var result = await registry.RunDockerCommandAsync(
                ["image", "inspect", image, "--format", "{{if .RepoDigests}}{{index .RepoDigests 0}}{{end}}"],
                token);
            return string.IsNullOrWhiteSpace(result.Output) ? null : result.Output.Trim();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to inspect Docker image digest for {Image}.", image);
            return null;
        }
    }

    static string BuildRegistryAddress(WorkerNode node)
    {
        var host = DockerImageRegistryService.NormalizeRegistryAddress(node.HostAddress);
        var colon = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host[(colon + 1)..], out _))
            host = host[..colon];

        return string.IsNullOrWhiteSpace(host) ? string.Empty : $"{host}:{node.RegistryPort}";
    }

    static string DigestHash(string digest)
    {
        var at = digest.LastIndexOf('@');
        return at >= 0 ? digest[(at + 1)..] : digest;
    }
}
