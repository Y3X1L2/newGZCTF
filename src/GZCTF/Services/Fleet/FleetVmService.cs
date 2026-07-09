using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Vm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public class FleetVmService
{
    private readonly FleetManager _fleetManager;
    private readonly AgentClient _agentClient;
    private readonly INodeRepository _nodeRepo;
    private readonly IVirtualMachineProvider _vmProvider;
    private readonly GuacamoleService _guacamoleService;
    private readonly ImageDistributionService _imageDistribution;
    private readonly KvmSettings _kvmSettings;
    private readonly AppDbContext _context;
    private readonly DeploymentQueueStateAccessor _queueState;
    private readonly DeploymentExecutionContextAccessor _executionContext;
    private readonly ILogger<FleetVmService> _logger;

    public FleetVmService(
        FleetManager fleetManager,
        AgentClient agentClient,
        INodeRepository nodeRepo,
        IVirtualMachineProvider vmProvider,
        GuacamoleService guacamoleService,
        ImageDistributionService imageDistribution,
        IOptions<KvmSettings> kvmSettings,
        AppDbContext context,
        DeploymentQueueStateAccessor queueState,
        DeploymentExecutionContextAccessor executionContext,
        ILogger<FleetVmService> logger)
    {
        _fleetManager = fleetManager;
        _agentClient = agentClient;
        _nodeRepo = nodeRepo;
        _vmProvider = vmProvider;
        _guacamoleService = guacamoleService;
        _imageDistribution = imageDistribution;
        _kvmSettings = kvmSettings.Value;
        _context = context;
        _queueState = queueState;
        _executionContext = executionContext;
        _logger = logger;
    }

    public async Task<VmInstance?> CreateVmAsync(VmInstance vmInstance, int? templateId, string? templatePath,
        int? memory, int? cpu, string? flag, CancellationToken token)
    {
        var gameId = vmInstance.Challenge?.GameId ??
                     await _context.GameChallenges.AsNoTracking()
                         .Where(c => c.Id == vmInstance.ChallengeId)
                         .Select(c => c.GameId)
                         .SingleOrDefaultAsync(token);

        var execution = _executionContext.Current;
        var capacityReservedByCaller = execution?.CapacityReserved == true;
        FleetScheduleResult? schedule = null;
        var nodeId = execution?.TargetNodeId;
        DeploymentTarget? executionTarget = null;

        if (nodeId is null)
        {
            var target = new DeploymentTarget
            {
                Type = TargetType.Vm,
                Action = TargetAction.Create,
                Payload = JsonSerializer.Serialize(new VmCreatePayload(
                    templateId, templatePath, memory, cpu, vmInstance.VmName, flag,
                    vmInstance.Id, gameId, vmInstance.UserId, vmInstance.ChallengeId))
            };
            schedule = await _fleetManager.TryScheduleWithTargetAsync(target, token);
            nodeId = schedule.NodeId;
        }
        else if (execution?.DeploymentTargetId is { } targetId)
        {
            executionTarget = await _context.DeploymentTargets
                .FirstOrDefaultAsync(t => t.Id == targetId, token);
        }

        if (nodeId is null)
        {
            if (schedule?.IsQueued == true)
            {
                _queueState.SetQueued(schedule.QueueStatus);
                _logger.LogInformation("VM creation queued: {Reason}", schedule.Reason);
            }
            else
            {
                vmInstance.Status = VmInstanceStatus.Error;
                _logger.LogWarning("No KVM node available for VM creation");
                if (schedule?.Target is not null)
                    _logger.SystemLogDeploymentTarget("not scheduled", schedule.Target);
                else
                    _logger.SystemLog("VM deployment failed: no KVM fleet node is available.",
                        TaskStatus.Failed, LogLevel.Warning);
            }

            return null;
        }

        var node = schedule?.Node ?? await _nodeRepo.GetNodeByIdAsync(nodeId.Value, token);
        var deploymentTarget = schedule?.Target ?? executionTarget;
        if (node?.IsLocal == true)
        {
            await UpdateTargetStage(deploymentTarget, TargetStatus.Creating, "creating", node, token);
            vmInstance.NodeId = nodeId.Value;
            var vm = await CreateLocalVmAsync(vmInstance, templatePath, memory, cpu, token);
            if (!capacityReservedByCaller && (vm is null || vm.Status == VmInstanceStatus.Error))
                FleetManager.ReleaseCapacity(node, NodeCapability.Kvm);
            else if (!capacityReservedByCaller && vm?.Status == VmInstanceStatus.Running)
                FleetManager.ConfirmCapacity(node, NodeCapability.Kvm);
            await CompleteTarget(deploymentTarget, vm, node.HostAddress, token);
            _logger.SystemLogDeploymentTarget(
                deploymentTarget?.Status == TargetStatus.Completed ? "completed" : "failed",
                deploymentTarget, node);
            return vm;
        }

        await UpdateTargetStage(deploymentTarget, TargetStatus.Running, "pulling image", node, token,
            "ensuring VM template on worker from storage registry");
        var imageReady = await EnsureRemoteVmImageAsync(nodeId.Value, node, templateId, token);
        if (!imageReady.Success)
        {
            _logger.LogWarning("Agent VM image ensure failed on node {NodeId}: {Message}",
                nodeId.Value, imageReady.Message);
            vmInstance.Status = VmInstanceStatus.Error;
            vmInstance.NodeId = nodeId.Value;
            if (!capacityReservedByCaller && node is not null)
                FleetManager.ReleaseCapacity(node, NodeCapability.Kvm);
            await FailTarget(deploymentTarget, imageReady.Message, token);
            _logger.SystemLogDeploymentTarget("failed", deploymentTarget, node);
            return null;
        }

        await UpdateTargetStage(deploymentTarget, TargetStatus.Creating, "creating", node, token,
            "VM image is ready on worker");
        var result = await _agentClient.CreateVmAsync(nodeId.Value, new AgentCreateVmRequest
        {
            TemplateId = templateId,
            ImageEnsured = true,
            VmName = vmInstance.VmName,
            Memory = memory ?? _kvmSettings.DefaultVmMemoryMb,
            Cpu = cpu ?? _kvmSettings.DefaultVmCpu,
            Flag = flag
        }, token);

        if (result is null)
        {
            _logger.LogWarning("Agent VM creation failed on node {NodeId}", nodeId.Value);
            vmInstance.Status = VmInstanceStatus.Error;
            vmInstance.NodeId = nodeId.Value;
            if (!capacityReservedByCaller && node is not null)
                FleetManager.ReleaseCapacity(node, NodeCapability.Kvm);
            await FailTarget(deploymentTarget, "Agent VM creation failed", token);
            _logger.SystemLogDeploymentTarget("failed", deploymentTarget, node);
            return null;
        }

        vmInstance.Status = VmInstanceStatus.Running;
        vmInstance.NodeId = nodeId.Value;
        if (!capacityReservedByCaller && node is not null)
            FleetManager.ConfirmCapacity(node, NodeCapability.Kvm);
        await CompleteTarget(deploymentTarget, vmInstance, node?.HostAddress, token);
        _logger.SystemLogDeploymentTarget("completed", deploymentTarget, node);
        return vmInstance;
    }

    private async Task<AgentVmImageDownloadResult> EnsureRemoteVmImageAsync(Guid nodeId, WorkerNode? node,
        int? templateId, CancellationToken token)
    {
        if (!templateId.HasValue)
            return AgentVmImageDownloadResult.Failed("VM template is required before creating a remote VM.");

        var result = await _imageDistribution.EnsureVmTemplateOnNodeAsync(templateId.Value, nodeId, token);
        if (!result.Success)
        {
            var nodeName = node?.Name ?? nodeId.ToString();
            return result with
            {
                Message = $"Node {nodeName} failed to ensure VM template {templateId.Value} from storage: {result.Message}"
            };
        }

        return result;
    }

    async Task UpdateTargetStage(DeploymentTarget? target, TargetStatus status, string stage,
        WorkerNode? node, CancellationToken token, string? detail = null)
    {
        if (target is null)
            return;

        target.Status = status;
        target.ErrorMessage = null;
        _logger.SystemLogDeploymentTarget(stage, target, node, detail);
        await _context.SaveChangesAsync(token);
    }

    private async Task<VmInstance?> CreateLocalVmAsync(VmInstance vmInstance, string? templatePath,
        int? memory, int? cpu, CancellationToken token)
    {
        if (string.IsNullOrEmpty(templatePath))
        {
            _logger.LogError("No template path provided for local VM creation");
            vmInstance.Status = VmInstanceStatus.Error;
            return vmInstance;
        }

        try
        {
            var createResult = await _vmProvider.CreateFromTemplateAsync(templatePath, vmInstance.VmName, memory, cpu, token);
            if (!createResult.Success)
            {
                _logger.LogError("Local VM creation failed for {VmName}: {Error}", vmInstance.VmName,
                    createResult.ErrorMessage);
                vmInstance.Status = VmInstanceStatus.Error;
                return vmInstance;
            }

            var startResult = await _vmProvider.StartAsync(vmInstance.VmName, token);
            if (!startResult.Success)
            {
                _logger.LogError("Local VM start failed for {VmName}: {Error}", vmInstance.VmName,
                    startResult.ErrorMessage);
                vmInstance.Status = VmInstanceStatus.Error;
                return vmInstance;
            }

            vmInstance.Status = VmInstanceStatus.Running;
            return vmInstance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local VM creation exception for {VmName}", vmInstance.VmName);
            vmInstance.Status = VmInstanceStatus.Error;
            return vmInstance;
        }
    }

    public async Task DestroyVmAsync(VmInstance vmInstance, CancellationToken token)
    {
        // Try to actually destroy the VM regardless of NodeId
        // If NodeId is null or points to a local node, destroy locally
        var isLocal = true;
        WorkerNode? node = null;
        if (vmInstance.NodeId.HasValue)
        {
            node = await _nodeRepo.GetNodeByIdAsync(vmInstance.NodeId.Value, token);
            isLocal = node?.IsLocal ?? true;
        }

        var hadCapacityReservation = vmInstance.NodeId.HasValue
            && vmInstance.Status is VmInstanceStatus.Creating or VmInstanceStatus.Running;
        var gameId = vmInstance.Challenge?.GameId;
        if (gameId is null)
        {
            var resolvedGameId = await _context.GameChallenges.AsNoTracking()
                .Where(c => c.Id == vmInstance.ChallengeId)
                .Select(c => c.GameId)
                .SingleOrDefaultAsync(token);
            if (resolvedGameId > 0)
                gameId = resolvedGameId;
        }

        var target = new DeploymentTarget
        {
            Type = TargetType.Vm,
            Action = TargetAction.Destroy,
            TargetNodeId = vmInstance.NodeId,
            Payload = JsonSerializer.Serialize(new VmDestroyPayload(
                vmInstance.Id,
                vmInstance.VmName,
                gameId,
                vmInstance.UserId,
                vmInstance.ChallengeId)),
            Status = TargetStatus.Creating,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DeploymentTargets.Add(target);

        if (isLocal)
        {
            try
            {
                _logger.SystemLog($"Destroying VM {vmInstance.VmName}.", TaskStatus.Pending, LogLevel.Information);
                _logger.LogInformation("Destroying local VM {VmName}", vmInstance.VmName);
                var result = await _vmProvider.DestroyAsync(vmInstance.VmName, token);
                if (!result.Success)
                    throw new InvalidOperationException(result.ErrorMessage ?? "Local VM destruction failed.");
            }
            catch (Exception ex)
            {
                target.Status = TargetStatus.Failed;
                target.CompletedAt = DateTimeOffset.UtcNow;
                target.ErrorMessage = ex.Message.Length <= 1024 ? ex.Message : ex.Message[..1024];
                await _context.SaveChangesAsync(token);
                _logger.SystemLog($"Failed to destroy VM {vmInstance.VmName}: {ex.Message}",
                    TaskStatus.Failed, LogLevel.Warning);
                _logger.LogWarning(ex, "Local VM destruction failed for {VmName}", vmInstance.VmName);
                throw;
            }
        }
        else
        {
            try
            {
                _logger.SystemLog($"Destroying VM {vmInstance.VmName}.", TaskStatus.Pending, LogLevel.Information);
                await _agentClient.DestroyVmAsync(vmInstance.NodeId!.Value, vmInstance.VmName, token);
            }
            catch (Exception ex)
            {
                target.Status = TargetStatus.Failed;
                target.CompletedAt = DateTimeOffset.UtcNow;
                target.ErrorMessage = ex.Message.Length <= 1024 ? ex.Message : ex.Message[..1024];
                await _context.SaveChangesAsync(token);
                _logger.SystemLog($"Failed to destroy VM {vmInstance.VmName}: {ex.Message}",
                    TaskStatus.Failed, LogLevel.Warning);
                _logger.LogWarning(ex, "Agent VM destruction failed for {VmName}", vmInstance.VmName);
                throw;
            }
        }

        if (!string.IsNullOrWhiteSpace(vmInstance.GuacamoleConnectionId))
        {
            var connectionId = vmInstance.GuacamoleConnectionId;
            var deleted = await _guacamoleService.DeleteConnectionAsync(connectionId, token);
            if (!deleted)
                _logger.LogWarning("Guacamole connection {ConnectionId} for VM {VmName} was not deleted.",
                    connectionId, vmInstance.VmName);
        }

        vmInstance.GuacamoleConnectionId = null;
        vmInstance.RdpUrl = null;
        vmInstance.Status = VmInstanceStatus.Destroyed;
        vmInstance.DestroyedAt = DateTimeOffset.UtcNow;
        target.Status = TargetStatus.Completed;
        target.CompletedAt = DateTimeOffset.UtcNow;

        if (hadCapacityReservation && node is not null)
        {
            FleetManager.ReleaseCurrentCapacity(node, NodeCapability.Kvm);
            await _context.SaveChangesAsync(token);
        }

        _logger.SystemLog($"Destroyed VM {vmInstance.VmName}.", TaskStatus.Success, LogLevel.Information);
    }

    private sealed record VmDestroyPayload(
        Guid VmInstanceId,
        string VmName,
        int? GameId,
        Guid UserId,
        int ChallengeId);

    private sealed record VmCreatePayload(
        int? TemplateId,
        string? TemplatePath,
        int? Memory,
        int? Cpu,
        string VmName,
        string? Flag,
        Guid VmInstanceId,
        int GameId,
        Guid UserId,
        int ChallengeId);

    async Task CompleteTarget(DeploymentTarget? target, VmInstance? vm, string? host, CancellationToken token)
    {
        if (target is null)
            return;

        target.CompletedAt = DateTimeOffset.UtcNow;
        if (vm is null || vm.Status == VmInstanceStatus.Error)
        {
            target.Status = TargetStatus.Failed;
            target.ErrorMessage = "VM creation failed";
        }
        else
        {
            target.Status = TargetStatus.Completed;
            target.ResultHost = host;
            target.ErrorMessage = null;
        }

        await _context.SaveChangesAsync(token);
    }

    async Task FailTarget(DeploymentTarget? target, string message, CancellationToken token)
    {
        if (target is null)
            return;

        target.Status = TargetStatus.Failed;
        target.CompletedAt = DateTimeOffset.UtcNow;
        target.ErrorMessage = message;
        await _context.SaveChangesAsync(token);
    }
}
