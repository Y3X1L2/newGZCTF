using System.Collections.Concurrent;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using DataContainer = GZCTF.Models.Data.Container;

namespace GZCTF.Services;

public class AwdpInstanceService(
    AppDbContext context,
    IAwdpRepository awdpRepository,
    IContainerManager containerManager,
    DockerImageRegistryService dockerRegistry,
    DeploymentQueueService deploymentQueue,
    DeploymentExecutionContextAccessor deploymentExecutionContext,
    INginxProxySyncService nginxProxySync,
    IServiceProvider serviceProvider,
    ILogger<AwdpInstanceService> logger)
{
    static readonly ConcurrentDictionary<int, SemaphoreSlim> InstanceLocks = new();

    public async Task CreateInstancesForGame(Game game, CancellationToken token = default)
    {
        var services = await awdpRepository.GetServicesByGame(game.Id, token);
        if (services.Length == 0)
            return;

        var participations = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == game.Id && p.Status == ParticipationStatus.Accepted)
            .OrderBy(p => p.TeamId)
            .ToArrayAsync(token);
        var createdAny = false;

        foreach (var service in services)
        {
            foreach (var part in participations)
            {
                if (await context.AwdpServiceInstances
                    .AnyAsync(i => i.ServiceId == service.Id && i.TeamId == part.TeamId, token))
                    continue;

                var networkName = GetNetworkName(game.Id, part.TeamId);
                var customNetwork = await TryCreateNetwork(networkName, token);

                var instance = new AwdpServiceInstance
                {
                    ServiceId = service.Id,
                    TeamId = part.TeamId,
                    NetworkName = customNetwork ? networkName : string.Empty,
                    IsRunning = false,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await context.AwdpServiceInstances.AddAsync(instance, token);
                await context.SaveChangesAsync(token);
                await deploymentQueue.EnqueueAsync(
                    DeploymentQueueRequest.AwdpContainer(part.TeamId, instance.Id) with
                    {
                        GameId = game.Id,
                        ChallengeId = service.Id,
                        SubjectType = "awdp-container",
                        SubjectPublicId = instance.Id.ToString(),
                        SubjectDisplayName = $"{game.Title} / team {part.TeamId}",
                        ResourceDisplayName = service.Name
                    }, token);
                createdAny = true;
            }
        }

        if (createdAny)
            await nginxProxySync.TrySyncNowAsync("AWDP containers created", token);
    }

    public async Task DestroyInstancesForGame(int gameId, CancellationToken token = default)
    {
        context.ChangeTracker.Clear();

        var instances = await context.AwdpServiceInstances
            .Include(i => i.Container)
            .Include(i => i.Service)
            .Where(i => i.Service.GameId == gameId)
            .ToArrayAsync(token);

        foreach (var instance in instances)
            await DestroyInstanceContainer(instance, token);

        await context.SaveChangesAsync(token);

        context.AwdpServiceInstances.RemoveRange(instances);
        await context.SaveChangesAsync(token);
        await nginxProxySync.TrySyncNowAsync("AWDP game instances destroyed", token);

        foreach (var networkName in instances.Select(i => i.NetworkName).Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct())
            await TryRemoveNetwork(networkName);
    }

    public async Task DestroyInstancesForService(int serviceId, CancellationToken token = default)
    {
        context.ChangeTracker.Clear();

        var instances = await context.AwdpServiceInstances
            .Include(i => i.Container)
            .Where(i => i.ServiceId == serviceId)
            .ToArrayAsync(token);

        foreach (var instance in instances)
            await DestroyInstanceContainer(instance, token);

        await context.SaveChangesAsync(token);

        context.AwdpServiceInstances.RemoveRange(instances);
        await context.SaveChangesAsync(token);
        await nginxProxySync.TrySyncNowAsync("AWDP service instances destroyed", token);

        foreach (var networkName in instances.Select(i => i.NetworkName).Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct())
            await TryRemoveNetwork(networkName);
    }

    public Task<(bool Success, string Message)> ResetInstance(int instanceId, string? newFlag = null,
        CancellationToken token = default) =>
        QueueResetAsync(instanceId, null, AwdpResetType.Admin, false, recordReset: true, token);

    public Task<(bool Success, string Message)> ResetInstanceForRound(int instanceId, string newFlag,
        CancellationToken token = default) =>
        QueueResetAsync(instanceId, null, AwdpResetType.Admin, false, recordReset: false, token);

    public Task<(bool Success, string Message)> ResetInstanceByPlayer(int instanceId, int teamId,
        CancellationToken token = default) =>
        QueueResetAsync(instanceId, teamId, AwdpResetType.Player, true, recordReset: true, token);

    async Task<(bool Success, string Message)> QueueResetAsync(
        int instanceId,
        int? expectedTeamId,
        AwdpResetType resetType,
        bool enforceLimit,
        bool recordReset,
        CancellationToken token)
    {
        var instance = await awdpRepository.GetInstanceForUpdate(instanceId, token);
        if (instance is null)
            return (false, "AWDP instance was not found.");
        if (expectedTeamId.HasValue && instance.TeamId != expectedTeamId.Value)
            return (false, "The AWDP instance belongs to another team.");
        if (instance.Container?.NodeId is not { } nodeId)
            return (false, "The AWDP instance has no running node and cannot be reset.");
        if (enforceLimit && await awdpRepository.GetResetCount(instance.ServiceId, instance.TeamId, token) >=
            instance.Service.MaxResetCount)
            return (false, "The AWDP reset limit has been reached.");

        if (recordReset)
        {
            await context.AwdpResetRecords.AddAsync(new AwdpResetRecord
            {
                ServiceId = instance.ServiceId,
                TeamId = instance.TeamId,
                ResetAt = DateTimeOffset.UtcNow,
                ResetType = resetType
            }, token);
            await context.SaveChangesAsync(token);
        }

        var generation = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.Kind == DeploymentQueueKind.AwdpContainer &&
                             ticket.AwdpServiceInstanceId == instanceId)
            .Select(ticket => (int?)ticket.Generation)
            .MaxAsync(token) ?? 0;
        var queued = await deploymentQueue.EnqueueAsync(
            DeploymentQueueRequest.AwdpContainer(instance.TeamId, instance.Id) with
            {
                GameId = instance.Service.GameId,
                ChallengeId = instance.ServiceId,
                Operation = RuntimeOperationKind.Reset,
                Generation = generation + 1,
                TargetNodeId = nodeId,
                SubjectType = "awdp-container",
                SubjectPublicId = instance.Id.ToString(),
                SubjectDisplayName = $"team {instance.TeamId}",
                ResourceDisplayName = instance.Service.Name
            }, token);
        return (true, $"AWDP reset queued as {queued.TicketId}.");
    }

    public Task<(bool Success, string Message)> RecoverInstanceByPlayer(int instanceId, int teamId,
        CancellationToken token = default) =>
        RunWithInstanceLock(instanceId, token,
            () => RecoverInstanceForTeam(instanceId, teamId, true, token));

    public Task<(bool Success, string Message)> RecoverInstance(int instanceId, CancellationToken token = default) =>
        RunWithInstanceLock(instanceId, token,
            () => RecoverInstanceForTeam(instanceId, null, false, token));

    static async Task<(bool Success, string Message)> RunWithInstanceLock(int instanceId, CancellationToken token,
        Func<Task<(bool Success, string Message)>> action)
    {
        var gate = InstanceLocks.GetOrAdd(instanceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);

        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    async Task<(bool Success, string Message)> ResetInstanceForTeam(int instanceId, int teamId,
        AwdpResetType resetType, bool enforceLimit, string? newFlag, CancellationToken token)
    {
        var instance = await awdpRepository.GetInstanceForUpdate(instanceId, token);
        if (instance is null)
            return (false, "实例未找到");

        if (instance.TeamId != teamId)
            return (false, "不能操作其他队伍的实例");

        var flagValue = newFlag ?? await GetCurrentFlagValue(instance, token);
        return await ResetLoadedInstance(instance, resetType, enforceLimit, flagValue, token);
    }

    async Task<(bool Success, string Message)> ResetInstance(int instanceId, AwdpResetType resetType,
        bool enforceLimit, string? newFlag, CancellationToken token, bool recordReset = true)
    {
        var instance = await awdpRepository.GetInstanceForUpdate(instanceId, token);
        if (instance is null)
            return (false, "实例未找到");

        var flagValue = newFlag ?? await GetCurrentFlagValue(instance, token);
        return await ResetLoadedInstance(instance, resetType, enforceLimit, flagValue, token, recordReset);
    }

    async Task<(bool Success, string Message)> ResetLoadedInstance(AwdpServiceInstance instance,
        AwdpResetType resetType, bool enforceLimit, string? newFlag, CancellationToken token,
        bool recordReset = true)
    {
        if (enforceLimit)
        {
            var resetCount = await awdpRepository.GetResetCount(instance.ServiceId, instance.TeamId, token);
            if (resetCount >= instance.Service.MaxResetCount)
                return (false, "重置次数已用尽");
        }

        var previousNodeId = instance.Container?.NodeId;
        await DestroyInstanceContainer(instance, token);
        await context.SaveChangesAsync(token);

        if (previousNodeId is null)
            return (false, "原实例缺少运行节点，无法重置");
        using var execution = deploymentExecutionContext.Push(
            new DeploymentExecutionContext(previousNodeId.Value, true, Guid.Empty));
        var container = await CreateContainer(instance.Service, instance.TeamId,
            string.IsNullOrWhiteSpace(instance.NetworkName) ? null : instance.NetworkName, newFlag, token);

        if (container is null)
        {
            instance.IsRunning = false;
            await context.SaveChangesAsync(token);
            return (false, "容器创建失败");
        }

        await context.Containers.AddAsync(container, token);

        instance.ContainerId = container.Id;
        instance.Container = container;
        instance.IsRunning = container.Status == ContainerStatus.Running;
        instance.CreatedAt = DateTimeOffset.UtcNow;

        if (recordReset)
            await context.AwdpResetRecords.AddAsync(new AwdpResetRecord
            {
                ServiceId = instance.ServiceId,
                TeamId = instance.TeamId,
                ResetAt = DateTimeOffset.UtcNow,
                ResetType = resetType
            }, token);

        await context.SaveChangesAsync(token);
        await nginxProxySync.TrySyncNowAsync("AWDP instance reset", token);
        return (true, "实例已重置");
    }

    async Task<(bool Success, string Message)> RecoverInstanceForTeam(int instanceId, int? teamId,
        bool enforceLimit, CancellationToken token)
    {
        var instance = await awdpRepository.GetInstanceForUpdate(instanceId, token);
        if (instance is null)
            return (false, "实例未找到");

        if (teamId.HasValue && instance.TeamId != teamId.Value)
            return (false, "不能操作其他队伍的实例");

        if (enforceLimit)
        {
            var recoveryCount = await awdpRepository.GetRecoveryCount(instance.ServiceId, instance.TeamId, token);
            if (recoveryCount >= instance.Service.MaxRecoveryCount)
                return (false, "恢复次数已用尽");
        }

        var currentRound = await awdpRepository.GetCurrentRound(instance.Service.GameId, token);
        var currentFlag = currentRound is null
            ? null
            : await awdpRepository.GetFlag(currentRound.Id, instance.ServiceId, instance.TeamId, token);

        var reset = await ResetLoadedInstance(instance, AwdpResetType.Player, false, currentFlag?.FlagValue, token,
            false);
        if (!reset.Success)
            return reset;

        await context.AwdpRecoveryRecords.AddAsync(new AwdpRecoveryRecord
        {
            ServiceId = instance.ServiceId,
            TeamId = instance.TeamId,
            RecoveryAt = DateTimeOffset.UtcNow
        }, token);

        await context.SaveChangesAsync(token);
        return (true, "实例已恢复");
    }

    async Task<string?> GetCurrentFlagValue(AwdpServiceInstance instance, CancellationToken token)
    {
        var currentRound = await awdpRepository.GetCurrentRound(instance.Service.GameId, token);
        if (currentRound is null)
            return null;

        var flag = await awdpRepository.GetFlag(currentRound.Id, instance.ServiceId, instance.TeamId, token);
        return flag?.FlagValue;
    }

    async Task<DataContainer?> CreateContainer(AwdpService service, int teamId, string? networkName, string? flag,
        CancellationToken token)
    {
        var image = await dockerRegistry.ResolveImageReferenceAsync(service.ImageName, token);
        var container = await containerManager.CreateContainerAsync(new ContainerConfig
        {
            Generation = deploymentExecutionContext.Current?.Generation ?? 1,
            Image = image,
            ExposedPort = service.ExposePort,
            CPUCount = 10,
            MemoryLimit = 512,
            StorageLimit = 512,
            Flag = flag,
            NetworkName = networkName,
            NetworkMode = string.IsNullOrWhiteSpace(networkName) ? NetworkMode.Isolated : NetworkMode.Custom,
            TeamId = teamId.ToString(),
            ChallengeId = service.Id,
            UserId = Guid.Empty,
            PreferredNodeId = deploymentExecutionContext.Current?.TargetNodeId,
            FleetCapacityReserved = deploymentExecutionContext.Current?.CapacityReserved == true
        }, token);

        if (container is not null && container.Id == Guid.Empty)
            container.Id = Guid.CreateVersion7();

        return container;
    }

    public async Task<bool> ExecuteQueuedCreateAsync(int instanceId, CancellationToken token)
    {
        var instance = await context.AwdpServiceInstances
            .Include(item => item.Service)
            .FirstOrDefaultAsync(item => item.Id == instanceId, token);
        if (instance is null)
            return false;
        if (instance.ContainerId is not null)
            return true;

        var flag = await GetCurrentFlagValue(instance, token);
        var container = await CreateContainer(instance.Service, instance.TeamId,
            string.IsNullOrWhiteSpace(instance.NetworkName) ? null : instance.NetworkName, flag, token);
        if (container is null)
            return false;
        if (container.Id == Guid.Empty)
            container.Id = Guid.CreateVersion7();
        await context.Containers.AddAsync(container, token);
        instance.Container = container;
        instance.ContainerId = container.Id;
        instance.IsRunning = container.Status == ContainerStatus.Running;
        instance.CreatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        await nginxProxySync.TrySyncNowAsync("AWDP queued container created", token);
        return instance.IsRunning;
    }

    public Task<(bool Success, string Message)> ExecuteQueuedResetAsync(int instanceId,
        CancellationToken token) =>
        RunWithInstanceLock(instanceId, token,
            () => ResetInstance(instanceId, AwdpResetType.Admin, false, null, token, recordReset: false));

    async Task DestroyInstanceContainer(AwdpServiceInstance instance, CancellationToken token)
    {
        if (instance.Container is null)
            return;

        try
        {
            await containerManager.DestroyContainerAsync(instance.Container, token);
            context.Containers.Remove(instance.Container);
            instance.ContainerId = null;
            instance.Container = null;
            instance.IsRunning = false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to destroy AWDP container for instance {InstanceId}", instance.Id);
            instance.IsRunning = false;
        }
    }

    async Task<bool> TryCreateNetwork(string networkName, CancellationToken token)
    {
        try
        {
            var containerOrchestrator = serviceProvider.GetService<ContainerOrchestrator>();
            if (containerOrchestrator is null)
                return false;

            await containerOrchestrator.CreateIsolatedNetwork(networkName, true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to create AWDP isolated network {NetworkName}; falling back to provider network",
                networkName);
            return false;
        }
    }

    async Task TryRemoveNetwork(string networkName)
    {
        try
        {
            var containerOrchestrator = serviceProvider.GetService<ContainerOrchestrator>();
            if (containerOrchestrator is null)
                return;

            await containerOrchestrator.RemoveNetwork(networkName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove AWDP isolated network {NetworkName}", networkName);
        }
    }

    static string GetNetworkName(int gameId, int teamId) => $"awdp-g{gameId}-t{teamId}";
}
