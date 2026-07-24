using GZCTF.Models.Request.Admin;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Infrastructure.Cache;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace GZCTF.Repositories;

public class ContainerRepository(
    IDistributedCache cache,
    IContainerManager service,
    IOptions<ContainerProvider> containerProvider,
    INginxProxySyncService nginxProxySync,
    ILogger<ContainerRepository> logger,
    AppDbContext context) : RepositoryBase(context), IContainerRepository
{
    public override Task<int> CountAsync(CancellationToken token = default) => Context.Containers.CountAsync(token);

    public Task<Container?> GetContainerById(Guid guid, CancellationToken token = default) =>
        Context.Containers.FirstOrDefaultAsync(i => i.Id == guid, token);

    public Task<Container?> GetContainerWithInstanceById(Guid guid, CancellationToken token = default) =>
        Context.Containers.IgnoreAutoIncludes()
            .Include(c => c.GameInstance).ThenInclude(i => i!.Challenge)
            .Include(c => c.GameInstance).ThenInclude(i => i!.FlagContext)
            .Include(c => c.GameInstance).ThenInclude(i => i!.Participation).ThenInclude(p => p.Team)
            .Include(c => c.GameInstance).ThenInclude(i => i!.Participation).ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(i => i.Id == guid, token);

    public async Task<ContainerInstanceModel[]> GetContainerInstances(CancellationToken token = default) =>
        (await Context.Containers
            .Where(c => c.GameInstance != null)
            .Include(c => c.GameInstance).ThenInclude(i => i!.Participation)
            .OrderBy(c => c.StartedAt).ToArrayAsync(token))
        .Select(ContainerInstanceModel.FromContainer)
        .ToArray();

    public Task<Container[]> GetDyingContainers(CancellationToken token = default) =>
        Context.Containers.Where(c => c.ExpectStopAt < DateTimeOffset.UtcNow).ToArrayAsync(token);

    public async Task<PortMappingEntry[]> GetProxyPortMappingsAsync(CancellationToken token = default)
    {
        var publicEntry = containerProvider.Value.PublicEntry;
        if (string.IsNullOrWhiteSpace(publicEntry))
            return [];

        return await Context.Containers
            .Where(c => c.Status == ContainerStatus.Running
                        && c.PublicPort != null
                        && c.PublicPortLeaseId != null
                        && c.PublicIP == publicEntry
                        && c.NodeId != null
                        && c.Node != null
                        && !c.IsProxy)
            .Select(c => new PortMappingEntry(c.PublicPort!.Value, c.IP, c.Port, c.PublicPortLeaseId!.Value))
            .ToArrayAsync(token);
    }

    public Task<int> SetEntryPublicationResultAsync(
        IReadOnlyCollection<Guid> leaseIds,
        ContainerEntryStatus status,
        string? error,
        CancellationToken token = default)
    {
        if (leaseIds.Count == 0)
            return Task.FromResult(0);

        var readyAt = status == ContainerEntryStatus.Ready ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
        var safeError = status == ContainerEntryStatus.Error ? PortMappingRevision.NormalizeError(error) : null;

        var query = Context.Containers
            .Where(container =>
                container.Status == ContainerStatus.Running &&
                container.PublicPortLeaseId != null &&
                leaseIds.Contains(container.PublicPortLeaseId.Value));

        // A failed candidate config is rolled back, so routes already confirmed by the
        // previous config remain usable. Only unpublished routes inherit the failure.
        if (status == ContainerEntryStatus.Error)
            query = query.Where(container => container.EntryStatus != ContainerEntryStatus.Ready);

        return query
            .ExecuteUpdateAsync(update => update
                .SetProperty(container => container.EntryStatus, status)
                .SetProperty(container => container.EntryReadyAt, readyAt)
                .SetProperty(container => container.EntryError, safeError), token);
    }

    public Task ExtendLifetime(Container container, TimeSpan time, CancellationToken token = default)
    {
        container.ExpectStopAt += time;
        logger.SystemLog(
            $"Extended Docker container lifetime: container={container.Id}, image={container.Image}, node={container.NodeId}, minutes={(int)time.TotalMinutes}.",
            TaskStatus.Success, LogLevel.Information);
        return SaveAsync(token);
    }

    public async Task<bool> ValidateContainer(Guid guid, CancellationToken token = default) =>
        await Context.Containers.AnyAsync(c => c.Id == guid, token);

    public async Task<bool> DestroyContainer(Container container, CancellationToken token = default)
    {
        logger.SystemLog(
            $"Destroying Docker container: container={container.Id}, image={container.Image}, node={container.NodeId}.",
            TaskStatus.Pending, LogLevel.Information);

        try
        {
            await service.DestroyContainerAsync(container, token);

            if (container.Status != ContainerStatus.Destroyed)
            {
                logger.SystemLog(
                    $"Docker container destroy failed: container={container.Id}, image={container.Image}, node={container.NodeId}, error=container manager did not report Destroyed status.",
                    TaskStatus.Failed, LogLevel.Warning);
                return false;
            }

            await cache.RemoveAsync(RuntimeCacheKeys.ConnectionCount(container.Id), token);

            await Context.GameInstances
                .Where(i => i.ContainerId == container.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ContainerId, (Guid?)null), token);
            await Context.ExerciseInstances
                .Where(i => i.ContainerId == container.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ContainerId, (Guid?)null), token);
            await Context.AwdpServiceInstances
                .Where(i => i.ContainerId == container.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ContainerId, (Guid?)null), token);
            await Context.GameChallenges
                .Where(c => c.TestContainerId == container.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.TestContainerId, (Guid?)null), token);

            Context.Containers.Remove(container);
            await SaveAsync(token);
            await nginxProxySync.TrySyncNowAsync("container destroyed", token);
            logger.SystemLog(
                $"Destroyed Docker container: container={container.Id}, image={container.Image}, node={container.NodeId}.",
                TaskStatus.Success, LogLevel.Information);

            return true;
        }
        catch (Exception ex)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerRepository_ContainerDestroyFailed),
                    container.LogId,
                    container.Image.Split("/").LastOrDefault() ?? "", ex.Message],
                TaskStatus.Failed, LogLevel.Warning);
            return false;
        }
    }

}
