using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Container.Manager;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public sealed record DeploymentExecutionResult(bool Success, string? ErrorMessage = null)
{
    public static DeploymentExecutionResult Completed() => new(true);

    public static DeploymentExecutionResult Failed(string? errorMessage) => new(false, errorMessage);
}

public class DeploymentExecutionService
{
    readonly AppDbContext _context;
    readonly IGameInstanceRepository _gameInstances;
    readonly IExerciseInstanceRepository _exerciseInstances;
    readonly IContainerRepository? _containers;
    readonly IContainerManager? _containerManager;
    readonly DockerImageRegistryService? _dockerRegistry;
    readonly INginxProxySyncService? _nginxProxySync;
    readonly DeploymentExecutionContextAccessor _executionContext;
    readonly FleetVmService? _fleetVmService;
    readonly ITeamLabRuntimeApplicationService? _teamLabRuntime;
    readonly AwdpInstanceService? _awdpInstances;
    readonly ILogger<DeploymentExecutionService> _logger;

    public DeploymentExecutionService(
        AppDbContext context,
        IGameInstanceRepository gameInstances,
        IExerciseInstanceRepository exerciseInstances,
        IContainerRepository containers,
        IContainerManager containerManager,
        DockerImageRegistryService dockerRegistry,
        INginxProxySyncService nginxProxySync,
        DeploymentExecutionContextAccessor executionContext,
        FleetVmService fleetVmService,
        ITeamLabRuntimeApplicationService teamLabRuntime,
        AwdpInstanceService awdpInstances,
        ILogger<DeploymentExecutionService> logger)
    {
        _context = context;
        _gameInstances = gameInstances;
        _exerciseInstances = exerciseInstances;
        _containers = containers;
        _containerManager = containerManager;
        _dockerRegistry = dockerRegistry;
        _nginxProxySync = nginxProxySync;
        _executionContext = executionContext;
        _fleetVmService = fleetVmService;
        _teamLabRuntime = teamLabRuntime;
        _awdpInstances = awdpInstances;
        _logger = logger;
    }

    public DeploymentExecutionService(AppDbContext context, ILogger<DeploymentExecutionService> logger)
    {
        _context = context;
        _gameInstances = null!;
        _exerciseInstances = null!;
        _containers = null;
        _containerManager = null;
        _dockerRegistry = null;
        _nginxProxySync = null;
        _executionContext = new DeploymentExecutionContextAccessor();
        _fleetVmService = null;
        _teamLabRuntime = null;
        _awdpInstances = null;
        _logger = logger;
    }

    public DeploymentExecutionService(AppDbContext context, FleetVmService fleetVmService,
        DeploymentExecutionContextAccessor executionContext,
        ILogger<DeploymentExecutionService> logger)
    {
        _context = context;
        _gameInstances = null!;
        _exerciseInstances = null!;
        _containers = null;
        _containerManager = null;
        _dockerRegistry = null;
        _nginxProxySync = null;
        _executionContext = executionContext;
        _fleetVmService = fleetVmService;
        _teamLabRuntime = null;
        _awdpInstances = null;
        _logger = logger;
    }

    protected DeploymentExecutionService()
    {
        _context = null!;
        _gameInstances = null!;
        _exerciseInstances = null!;
        _containers = null;
        _executionContext = null!;
        _awdpInstances = null;
        _logger = null!;
    }

    public virtual async Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
        CancellationToken token) =>
        ticket.Operation == RuntimeOperationKind.Create
            ? ticket.Kind switch
            {
                DeploymentQueueKind.GameContainer => await ExecuteGameContainerAsync(ticket, token),
                DeploymentQueueKind.ExerciseContainer => await ExecuteExerciseContainerAsync(ticket, token),
                DeploymentQueueKind.TrainingContainer => await ExecuteExerciseContainerAsync(ticket, token),
                DeploymentQueueKind.AwdpContainer => await ExecuteAwdpContainerAsync(ticket, token),
                DeploymentQueueKind.ChallengeTestContainer => await ExecuteChallengeTestContainerAsync(ticket, token),
                DeploymentQueueKind.VirtualMachine => await ExecuteVmAsync(ticket, token),
                DeploymentQueueKind.TeamLabRuntime => await ExecuteTeamLabRuntimeAsync(ticket, token),
                _ => DeploymentExecutionResult.Failed($"Unsupported deployment queue kind {ticket.Kind}.")
            }
            : await ExecuteControlAsync(ticket, token);

    async Task<DeploymentExecutionResult> ExecuteControlAsync(DeploymentQueueTicket ticket,
        CancellationToken token) => ticket.Kind switch
        {
            DeploymentQueueKind.GameContainer => await ExecuteGameContainerControlAsync(ticket, token),
            DeploymentQueueKind.ExerciseContainer or DeploymentQueueKind.TrainingContainer =>
                await ExecuteExerciseContainerControlAsync(ticket, token),
            DeploymentQueueKind.VirtualMachine => await ExecuteVmControlAsync(ticket, token),
            DeploymentQueueKind.TeamLabRuntime => await ExecuteTeamLabControlAsync(ticket, token),
            DeploymentQueueKind.AwdpContainer => await ExecuteAwdpControlAsync(ticket, token),
            DeploymentQueueKind.ChallengeTestContainer => ticket.SubjectType == "challenge-test-container"
                ? await ExecuteChallengeTestContainerControlAsync(ticket, token)
                : await ExecuteMaintenanceContainerAsync(ticket, token),
            _ => DeploymentExecutionResult.Failed($"Unsupported deployment queue kind {ticket.Kind}.")
        };

    async Task<DeploymentExecutionResult> ExecuteMaintenanceContainerAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_containers is null || !Guid.TryParse(ticket.SubjectPublicId, out var containerId))
            return DeploymentExecutionResult.Failed("Maintenance container ticket has invalid identity.");
        var container = await _context.Containers.SingleOrDefaultAsync(item => item.Id == containerId, token);
        return await ExecuteContainerControlAsync(ticket, container, token);
    }

    async Task<DeploymentExecutionResult> ExecuteChallengeTestContainerControlAsync(
        DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_containers is null || ticket.GameId is not { } gameId || ticket.ChallengeId is not { } challengeId)
            return DeploymentExecutionResult.Failed("Challenge test container control ticket has invalid identity.");
        var container = await _context.GameChallenges
            .Where(challenge => challenge.GameId == gameId && challenge.Id == challengeId)
            .Select(challenge => challenge.TestContainer)
            .SingleOrDefaultAsync(token);
        return await ExecuteContainerControlAsync(ticket, container, token);
    }

    async Task<DeploymentExecutionResult> ExecuteChallengeTestContainerAsync(
        DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_containerManager is null || _dockerRegistry is null ||
            ticket.GameId is not { } gameId || ticket.ChallengeId is not { } challengeId ||
            ticket.OwnerUserId is not { } userId || ticket.TargetNodeId is not { } nodeId)
            return DeploymentExecutionResult.Failed("Challenge test container ticket has invalid identity.");

        var challenge = await _context.GameChallenges
            .Include(item => item.TestContainer)
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.Id == challengeId, token);
        if (challenge is null)
            return DeploymentExecutionResult.Failed("Challenge test container references a missing challenge.");
        if (challenge.TestContainer is { Status: ContainerStatus.Running })
            return DeploymentExecutionResult.Completed();
        if (!challenge.Type.IsContainer() || challenge.Environment != EnvironmentType.Docker ||
            string.IsNullOrWhiteSpace(challenge.ContainerImage) || challenge.ExposePort is not (>= 1 and <= 65535))
            return DeploymentExecutionResult.Failed("Challenge test container runtime configuration is invalid.");

        var image = await _dockerRegistry.ResolveImageReferenceAsync(challenge.ContainerImage, token);
        using var _ = _executionContext.Push(new DeploymentExecutionContext(
            nodeId, CapacityReserved: true, ticket.Id, ticket.Generation));
        var container = await _containerManager.CreateContainerAsync(new ContainerConfig
        {
            Generation = ticket.Generation,
            TeamId = "admin",
            UserId = userId,
            ChallengeId = challenge.Id,
            Flag = challenge.Type.IsDynamic() ? challenge.GenerateTestFlag() : null,
            Image = image,
            CPUCount = challenge.CPUCount ?? 1,
            MemoryLimit = challenge.MemoryLimit ?? 64,
            StorageLimit = challenge.StorageLimit ?? 256,
            NetworkMode = challenge.NetworkMode ?? NetworkMode.Open,
            ExposedPort = challenge.ExposePort.Value,
            PreferredNodeId = nodeId,
            FleetCapacityReserved = true
        }, token);
        if (container is null)
            return DeploymentExecutionResult.Failed("Challenge test container creation failed.");

        challenge.TestContainer = container;
        await _context.SaveChangesAsync(token);
        if (_nginxProxySync is not null)
            await _nginxProxySync.TrySyncNowAsync("test container created", token);
        _logger.SystemLog(
            $"Challenge test container created: ticket={ticket.Id}, game={gameId}, challenge={challengeId}, node={nodeId}, container={container.LogId}.",
            TaskStatus.Success,
            LogLevel.Information);
        return DeploymentExecutionResult.Completed();
    }

    async Task<DeploymentExecutionResult> ExecuteGameContainerControlAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_containers is null || ticket.GameId is not { } gameId ||
            ticket.OwnerTeamId is not { } teamId || ticket.ChallengeId is not { } challengeId)
            return DeploymentExecutionResult.Failed("Game container control ticket has invalid identity.");
        var instance = await _context.GameInstances.Include(item => item.Container)
            .SingleOrDefaultAsync(item => item.ChallengeId == challengeId &&
                                          item.Participation.GameId == gameId &&
                                          item.Participation.TeamId == teamId, token);
        return await ExecuteContainerControlAsync(ticket, instance?.Container, token);
    }

    async Task<DeploymentExecutionResult> ExecuteExerciseContainerControlAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_containers is null || ticket.OwnerUserId is not { } userId ||
            ticket.ChallengeId is not { } challengeId)
            return DeploymentExecutionResult.Failed("Exercise container control ticket has invalid identity.");
        var instance = await _context.ExerciseInstances.Include(item => item.Container)
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ExerciseId == challengeId, token);
        return await ExecuteContainerControlAsync(ticket, instance?.Container, token);
    }

    async Task<DeploymentExecutionResult> ExecuteContainerControlAsync(DeploymentQueueTicket ticket,
        Models.Data.Container? container, CancellationToken token)
    {
        if (_containers is null)
            return DeploymentExecutionResult.Failed("Container control service is unavailable.");
        if (container is null || container.Status == ContainerStatus.Destroyed)
            return ticket.Operation is RuntimeOperationKind.Stop or RuntimeOperationKind.Destroy
                ? DeploymentExecutionResult.Completed()
                : DeploymentExecutionResult.Failed("The runtime container no longer exists.");
        if (container.RuntimeGeneration != ticket.Generation)
            return DeploymentExecutionResult.Failed("Container control ticket has a stale runtime generation.");
        if (ticket.Operation == RuntimeOperationKind.Extend)
        {
            if (ticket.ExtensionSeconds is not > 0)
                return DeploymentExecutionResult.Failed("Container extension duration is invalid.");
            await _containers.ExtendLifetime(container, TimeSpan.FromSeconds(ticket.ExtensionSeconds.Value), token);
            return DeploymentExecutionResult.Completed();
        }
        if (ticket.Operation is RuntimeOperationKind.Stop or RuntimeOperationKind.Destroy)
            return await _containers.DestroyContainer(container, token)
                ? DeploymentExecutionResult.Completed()
                : DeploymentExecutionResult.Failed("Container destruction failed.");
        return DeploymentExecutionResult.Failed($"Container operation {ticket.Operation} is not supported.");
    }

    async Task<DeploymentExecutionResult> ExecuteVmControlAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_fleetVmService is null || ticket.VmInstanceId is not { } vmId)
            return DeploymentExecutionResult.Failed("VM control ticket has invalid identity.");
        var vm = await _context.VmInstances.SingleOrDefaultAsync(item => item.Id == vmId, token);
        if (vm is null || vm.Status == VmInstanceStatus.Destroyed)
            return ticket.Operation is RuntimeOperationKind.Stop or RuntimeOperationKind.Destroy
                ? DeploymentExecutionResult.Completed()
                : DeploymentExecutionResult.Failed("The VM no longer exists.");
        if (vm.RuntimeGeneration != ticket.Generation)
            return DeploymentExecutionResult.Failed("VM control ticket has a stale runtime generation.");
        if (ticket.Operation is not (RuntimeOperationKind.Stop or RuntimeOperationKind.Destroy))
            return DeploymentExecutionResult.Failed($"VM operation {ticket.Operation} is not supported.");
        await _fleetVmService.DestroyVmAsync(vm, token);
        return vm.Status == VmInstanceStatus.Destroyed
            ? DeploymentExecutionResult.Completed()
            : DeploymentExecutionResult.Failed("VM destruction failed.");
    }

    async Task<DeploymentExecutionResult> ExecuteTeamLabControlAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_teamLabRuntime is null || ticket.TeamLabRuntimeId is not { } runtimeId)
            return DeploymentExecutionResult.Failed("TeamLab control ticket has invalid identity.");
        var runtimeGeneration = await _context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.Id == runtimeId)
            .Select(item => (int?)item.Generation)
            .SingleOrDefaultAsync(token);
        if (runtimeGeneration is not null && runtimeGeneration != ticket.Generation)
            return DeploymentExecutionResult.Failed("TeamLab control ticket has a stale runtime generation.");
        return ticket.Operation switch
        {
            RuntimeOperationKind.Reset => await _teamLabRuntime.ExecuteQueuedResetAsync(
                runtimeId, ticket.Id, ticket.ProtectedPayload, token) is { } result && result.Success
                ? DeploymentExecutionResult.Completed()
                : DeploymentExecutionResult.Failed("TeamLab reset deployment failed."),
            RuntimeOperationKind.Stop or RuntimeOperationKind.Destroy =>
                (await _teamLabRuntime.ExecuteQueuedDestroyAsync(runtimeId, token)).Success
                    ? DeploymentExecutionResult.Completed()
                    : DeploymentExecutionResult.Failed("TeamLab cleanup failed."),
            _ => DeploymentExecutionResult.Failed($"TeamLab operation {ticket.Operation} is not supported.")
        };
    }

    async Task<DeploymentExecutionResult> ExecuteAwdpControlAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_awdpInstances is null || ticket.AwdpServiceInstanceId is not { } instanceId ||
            ticket.Operation != RuntimeOperationKind.Reset)
            return DeploymentExecutionResult.Failed("AWDP control ticket is invalid.");
        var result = await _awdpInstances.ExecuteQueuedResetAsync(instanceId, token);
        return result.Success ? DeploymentExecutionResult.Completed() : DeploymentExecutionResult.Failed(result.Message);
    }

    async Task<DeploymentExecutionResult> ExecuteAwdpContainerAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (_awdpInstances is null || ticket.AwdpServiceInstanceId is not { } instanceId)
            return DeploymentExecutionResult.Failed("AWDP queue ticket is missing its service instance.");
        if (ticket.TargetNodeId is not { } nodeId)
            return DeploymentExecutionResult.Failed("Deployment queue ticket has no assigned target node.");
        using var _ = _executionContext.Push(new DeploymentExecutionContext(nodeId, true, ticket.Id,
            ticket.Generation));
        return await _awdpInstances.ExecuteQueuedCreateAsync(instanceId, token)
            ? DeploymentExecutionResult.Completed()
            : DeploymentExecutionResult.Failed("AWDP container creation failed.");
    }

    async Task<DeploymentExecutionResult> ExecuteGameContainerAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.GameId is not { } gameId ||
            ticket.OwnerTeamId is not { } teamId ||
            ticket.ChallengeId is not { } challengeId)
            return DeploymentExecutionResult.Failed("Game container queue ticket is missing required identity fields.");

        var instance = await _context.GameInstances
            .Include(i => i.FlagContext)
            .Include(i => i.Container)
            .Include(i => i.Challenge).ThenInclude(c => c.Flags)
            .Include(i => i.Participation)
                .ThenInclude(p => p.Team)
            .Include(i => i.Participation)
                .ThenInclude(p => p.Game)
            .Where(i => i.ChallengeId == challengeId &&
                        i.Participation.GameId == gameId &&
                        i.Participation.TeamId == teamId)
            .SingleOrDefaultAsync(token);

        if (instance is null)
        {
            _logger.LogWarning(
                "Game container queue ticket {TicketId} references missing instance: Game={GameId}, Team={TeamId}, Challenge={ChallengeId}",
                ticket.Id, gameId, teamId, challengeId);
            return DeploymentExecutionResult.Failed(
                $"Game instance was not found for game {gameId}, team {teamId}, challenge {challengeId}.");
        }

        var user = await _context.UserParticipations
            .Where(p => p.GameId == gameId && p.TeamId == teamId)
            .OrderBy(p => p.UserId)
            .Select(p => p.User)
            .FirstOrDefaultAsync(token);
        if (user is null)
            return DeploymentExecutionResult.Failed(
                $"No team member was found for game {gameId}, team {teamId}.");

        if (ticket.TargetNodeId is not { } nodeId)
            return DeploymentExecutionResult.Failed("Deployment queue ticket has no assigned target node.");

        using var _ = _executionContext.Push(new DeploymentExecutionContext(nodeId, CapacityReserved: true,
            ticket.Id, ticket.Generation));
        var result = await _gameInstances.CreateContainer(
            instance,
            instance.Participation.Team,
            user,
            instance.Participation.Game,
            token);

        return result.Status == TaskStatus.Success
            ? DeploymentExecutionResult.Completed()
            : DeploymentExecutionResult.Failed($"Game container creation returned {result.Status}.");
    }

    async Task<DeploymentExecutionResult> ExecuteExerciseContainerAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.OwnerUserId is not { } userId || ticket.ChallengeId is not { } challengeId)
            return DeploymentExecutionResult.Failed(
                "Exercise container queue ticket is missing required identity fields.");

        var instance = await _context.ExerciseInstances
            .Include(i => i.FlagContext)
            .Include(i => i.Container)
            .Include(i => i.Exercise).ThenInclude(e => e.Flags)
            .SingleOrDefaultAsync(i => i.UserId == userId && i.ExerciseId == challengeId, token);

        if (instance is null)
        {
            _logger.LogWarning(
                "Exercise container queue ticket {TicketId} references missing instance: User={UserId}, Exercise={ExerciseId}",
                ticket.Id, userId, challengeId);
            return DeploymentExecutionResult.Failed(
                $"Exercise instance was not found for user {userId}, exercise {challengeId}.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, token);
        if (user is null)
            return DeploymentExecutionResult.Failed($"Exercise user {userId} was not found.");

        if (ticket.TargetNodeId is not { } nodeId)
            return DeploymentExecutionResult.Failed("Deployment queue ticket has no assigned target node.");

        using var _ = _executionContext.Push(new DeploymentExecutionContext(nodeId, CapacityReserved: true,
            ticket.Id, ticket.Generation));
        var result = await _exerciseInstances.CreateContainer(instance, user, token);

        return result.Status == TaskStatus.Success
            ? DeploymentExecutionResult.Completed()
            : DeploymentExecutionResult.Failed($"Exercise container creation returned {result.Status}.");
    }

    async Task<DeploymentExecutionResult> ExecuteVmAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (_fleetVmService is null)
            return DeploymentExecutionResult.Failed("VM queue executor is not available.");

        if (ticket.VmInstanceId is not { } vmInstanceId ||
            ticket.GameId is not { } gameId ||
            ticket.OwnerUserId is not { } userId ||
            ticket.ChallengeId is not { } challengeId)
            return DeploymentExecutionResult.Failed("VM queue ticket is missing required identity fields.");

        var vm = await _context.VmInstances
            .Include(v => v.Challenge!)
                .ThenInclude(c => c.ImageTemplate)
            .SingleOrDefaultAsync(v => v.Id == vmInstanceId, token);

        if (vm is null)
        {
            _logger.LogWarning("VM queue ticket {TicketId} references missing VM instance {VmInstanceId}",
                ticket.Id, vmInstanceId);
            return DeploymentExecutionResult.Failed($"VM instance {vmInstanceId} was not found.");
        }

        if (vm.UserId != userId || vm.ChallengeId != challengeId || vm.Challenge?.GameId != gameId)
            return DeploymentExecutionResult.Failed("VM queue ticket identity does not match the VM instance.");

        if (ticket.TargetNodeId is not { } nodeId)
            return DeploymentExecutionResult.Failed("Deployment queue ticket has no assigned target node.");

        var challenge = vm.Challenge;
        var templateId = challenge?.ImageTemplateId;
        var templatePath = challenge?.ImageTemplate?.LocalFilePath;
        var memory = ResolveVmMemory(challenge?.MemoryLimit);
        var cpu = ResolveVmCpu(challenge?.CPUCount);

        var previousNode = vm.NodeId;
        using var _ = _executionContext.Push(new DeploymentExecutionContext(nodeId, CapacityReserved: true,
            ticket.Id, ticket.Generation));
        var result = await _fleetVmService.CreateVmAsync(vm, templateId, templatePath, memory, cpu, flag: null, token);

        if (result is null || vm.Status == VmInstanceStatus.Error)
        {
            if (previousNode is null)
                vm.NodeId = null;

            return DeploymentExecutionResult.Failed("Queued VM creation failed.");
        }

        return DeploymentExecutionResult.Completed();
    }

    async Task<DeploymentExecutionResult> ExecuteTeamLabRuntimeAsync(DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.TeamLabRuntimeId is not { } runtimeId)
            return DeploymentExecutionResult.Failed("TeamLab runtime queue ticket is missing its runtime identity.");

        var runtime = await _context.TeamLabRuntimes
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == runtimeId, token);
        if (runtime is null)
            return DeploymentExecutionResult.Failed($"TeamLab runtime {runtimeId} was not found.");

        if (_teamLabRuntime is null)
            return DeploymentExecutionResult.Failed("TeamLab runtime queue executor is not available.");

        var result = await _teamLabRuntime.ExecuteQueuedAsync(runtimeId, token);
        return result.Success
            ? DeploymentExecutionResult.Completed()
            : DeploymentExecutionResult.Failed(result.Message);
    }

    static int? ResolveVmMemory(int? memoryLimit) => memoryLimit is >= 1024 ? memoryLimit : null;

    static int? ResolveVmCpu(int? cpuCount) => cpuCount is >= 1 ? cpuCount : null;
}
