using GZCTF.Models.Request.Admin;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
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
                        && c.PublicIP == publicEntry
                        && c.NodeId != null
                        && c.Node != null
                        && !c.Node.IsLocal
                        && !c.IsProxy)
            .Select(c => new PortMappingEntry(c.PublicPort!.Value, c.IP, c.Port))
            .ToArrayAsync(token);
    }

    public Task ExtendLifetime(Container container, TimeSpan time, CancellationToken token = default)
    {
        container.ExpectStopAt += time;
        return SaveAsync(token);
    }

    public async Task<bool> ValidateContainer(Guid guid, CancellationToken token = default) =>
        await Context.Containers.AnyAsync(c => c.Id == guid, token);

    public async Task<bool> DestroyContainer(Container container, CancellationToken token = default)
    {
        try
        {
            await service.DestroyContainerAsync(container, token);

            if (container.Status != ContainerStatus.Destroyed)
                return false;

            await cache.RemoveAsync(CacheKey.ConnectionCount(container.Id), token);

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
