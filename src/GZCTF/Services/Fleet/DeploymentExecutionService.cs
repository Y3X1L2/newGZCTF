using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using GZCTF.Services.TeamLab;
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
    readonly DeploymentExecutionContextAccessor _executionContext;
    readonly FleetVmService? _fleetVmService;
    readonly TeamLabDeploymentService? _teamLabDeployment;
    readonly ILogger<DeploymentExecutionService> _logger;

    public DeploymentExecutionService(
        AppDbContext context,
        IGameInstanceRepository gameInstances,
        IExerciseInstanceRepository exerciseInstances,
        DeploymentExecutionContextAccessor executionContext,
        FleetVmService fleetVmService,
        TeamLabDeploymentService teamLabDeployment,
        ILogger<DeploymentExecutionService> logger)
    {
        _context = context;
        _gameInstances = gameInstances;
        _exerciseInstances = exerciseInstances;
        _executionContext = executionContext;
        _fleetVmService = fleetVmService;
        _teamLabDeployment = teamLabDeployment;
        _logger = logger;
    }

    public DeploymentExecutionService(AppDbContext context, ILogger<DeploymentExecutionService> logger)
    {
        _context = context;
        _gameInstances = null!;
        _exerciseInstances = null!;
        _executionContext = new DeploymentExecutionContextAccessor();
        _fleetVmService = null;
        _teamLabDeployment = null;
        _logger = logger;
    }

    public DeploymentExecutionService(AppDbContext context, FleetVmService fleetVmService,
        DeploymentExecutionContextAccessor executionContext,
        ILogger<DeploymentExecutionService> logger)
    {
        _context = context;
        _gameInstances = null!;
        _exerciseInstances = null!;
        _executionContext = executionContext;
        _fleetVmService = fleetVmService;
        _teamLabDeployment = null;
        _logger = logger;
    }

    protected DeploymentExecutionService()
    {
        _context = null!;
        _gameInstances = null!;
        _exerciseInstances = null!;
        _executionContext = null!;
        _logger = null!;
    }

    public virtual async Task<DeploymentExecutionResult> ExecuteAsync(DeploymentQueueTicket ticket,
        CancellationToken token) =>
        ticket.Kind switch
        {
            DeploymentQueueKind.GameContainer => await ExecuteGameContainerAsync(ticket, token),
            DeploymentQueueKind.ExerciseContainer => await ExecuteExerciseContainerAsync(ticket, token),
            DeploymentQueueKind.Vm => await ExecuteVmAsync(ticket, token),
            DeploymentQueueKind.TeamLabRuntime => await ExecuteTeamLabRuntimeAsync(ticket, token),
            _ => DeploymentExecutionResult.Failed($"Unsupported deployment queue kind {ticket.Kind}.")
        };

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

        using var _ = _executionContext.Push(new DeploymentExecutionContext(nodeId, CapacityReserved: true));
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

        using var _ = _executionContext.Push(new DeploymentExecutionContext(nodeId, CapacityReserved: true));
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
        using var _ = _executionContext.Push(new DeploymentExecutionContext(nodeId, CapacityReserved: true));
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
        if (ticket.TeamLabRuntimeId is not { } runtimeId ||
            ticket.GameId is not { } gameId ||
            ticket.OwnerTeamId is not { } teamId)
            return DeploymentExecutionResult.Failed("TeamLab runtime queue ticket is missing required identity fields.");

        var runtime = await _context.TeamLabRuntimes
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == runtimeId, token);
        if (runtime is null)
            return DeploymentExecutionResult.Failed($"TeamLab runtime {runtimeId} was not found.");

        if (runtime.GameId != gameId || runtime.TeamId != teamId)
            return DeploymentExecutionResult.Failed("TeamLab runtime queue ticket identity does not match the runtime.");

        if (_teamLabDeployment is null)
            return DeploymentExecutionResult.Failed("TeamLab runtime queue executor is not available.");

        var result = await _teamLabDeployment.DeployQueuedRuntimeAsync(runtimeId, token);
        return result.Success
            ? DeploymentExecutionResult.Completed()
            : DeploymentExecutionResult.Failed(result.Message);
    }

    static int? ResolveVmMemory(int? memoryLimit) => memoryLimit is >= 1024 ? memoryLimit : null;

    static int? ResolveVmCpu(int? cpuCount) => cpuCount is >= 1 ? cpuCount : null;
}
