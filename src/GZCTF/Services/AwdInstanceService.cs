using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Container.Manager;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class AwdInstanceService(
    AppDbContext context,
    IAwdRepository awdRepository,
    IContainerManager containerManager,
    ContainerOrchestrator containerOrchestrator,
    ILogger<AwdInstanceService> logger)
{
    public async Task CreateInstancesForGame(Game game, CancellationToken token = default)
    {
        var services = await awdRepository.GetServicesByGame(game.Id, token);
        var participations = game.Participations.Where(p => p.Status == ParticipationStatus.Accepted).ToList();

        foreach (var service in services)
        {
            foreach (var part in participations)
            {
                var networkName = $"awd-team-{part.TeamId}";
                await containerOrchestrator.CreateIsolatedNetwork(networkName);

                var container = await containerManager.CreateContainerAsync(new ContainerConfig
                {
                    Image = service.ImageName,
                    ExposedPort = service.ExposePort,
                    CPUCount = 10,
                    MemoryLimit = 512,
                    Flag = "PLACEHOLDER",
                    NetworkName = networkName,
                    TeamId = part.TeamId.ToString(),
                    ChallengeId = service.Id,
                    UserId = Guid.Empty
                }, token);

                if (container is null)
                {
                    logger.LogError("Failed to create container for service {ServiceId}, team {TeamId}",
                        service.Id, part.TeamId);
                    continue;
                }

                await context.Containers.AddAsync(container, token);
                await context.SaveChangesAsync(token);

                var instance = new AwdServiceInstance
                {
                    ServiceId = service.Id,
                    TeamId = part.TeamId,
                    ContainerId = container.Id,
                    NetworkName = networkName,
                    IsRunning = true
                };

                await context.AwdServiceInstances.AddAsync(instance, token);
            }
        }

        await context.SaveChangesAsync(token);
    }

    public async Task DestroyInstancesForGame(int gameId, CancellationToken token = default)
    {
        var instances = await awdRepository.GetInstancesByGame(gameId, token);
        foreach (var instance in instances)
        {
            if (instance.Container is not null)
            {
                await containerManager.DestroyContainerAsync(instance.Container, token);
            }

            try
            {
                await containerOrchestrator.RemoveNetwork(instance.NetworkName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove network {Network}", instance.NetworkName);
            }
        }

        context.AwdServiceInstances.RemoveRange(instances);
        await context.SaveChangesAsync(token);
    }

    public async Task ResetInstance(int instanceId, string? newFlag = null, CancellationToken token = default)
    {
        var instance = await awdRepository.GetInstance(instanceId, token);
        if (instance?.Container is null) return;

        await containerManager.DestroyContainerAsync(instance.Container, token);

        var container = await containerManager.CreateContainerAsync(new ContainerConfig
        {
            Image = instance.Service.ImageName,
            ExposedPort = instance.Service.ExposePort,
            Flag = newFlag ?? "PLACEHOLDER",
            NetworkName = instance.NetworkName,
            TeamId = instance.TeamId.ToString(),
            ChallengeId = instance.Service.Id,
            UserId = Guid.Empty
        }, token);

        if (container is not null)
        {
            await context.Containers.AddAsync(container, token);
            await context.SaveChangesAsync(token);

            instance.ContainerId = container.Id;
            instance.IsRunning = true;
            context.AwdServiceInstances.Update(instance);
            await context.SaveChangesAsync(token);
        }
    }
}
