using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Vm;
using GZCTF.Modules.Runtime.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public class FleetVmService
{
    private readonly AgentClient _agentClient;
    private readonly INodeRepository _nodeRepo;
    private readonly IVirtualMachineProvider _vmProvider;
    private readonly VmCredentialService _credentialService;
    private readonly GuacamoleService _guacamoleService;
    private readonly ImageDistributionService _imageDistribution;
    private readonly KvmSettings _kvmSettings;
    private readonly AppDbContext _context;
    private readonly DeploymentQueueService _queue;
    private readonly DeploymentExecutionContextAccessor _executionContext;
    private readonly ILogger<FleetVmService> _logger;

    public FleetVmService(
        AgentClient agentClient,
        INodeRepository nodeRepo,
        IVirtualMachineProvider vmProvider,
        VmCredentialService credentialService,
        GuacamoleService guacamoleService,
        ImageDistributionService imageDistribution,
        IOptions<KvmSettings> kvmSettings,
        AppDbContext context,
        DeploymentQueueService queue,
        DeploymentExecutionContextAccessor executionContext,
        ILogger<FleetVmService> logger)
    {
        _agentClient = agentClient;
        _nodeRepo = nodeRepo;
        _vmProvider = vmProvider;
        _credentialService = credentialService;
        _guacamoleService = guacamoleService;
        _imageDistribution = imageDistribution;
        _kvmSettings = kvmSettings.Value;
        _context = context;
        _queue = queue;
        _executionContext = executionContext;
        _logger = logger;
    }

    public async Task<VmInstance?> CreateVmAsync(VmInstance vmInstance, int? templateId,
        int? memory, int? cpu, string? flag, CancellationToken token)
    {
        var gameId = vmInstance.Challenge?.GameId ??
                     await _context.GameChallenges.AsNoTracking()
                         .Where(c => c.Id == vmInstance.ChallengeId)
                         .Select(c => c.GameId)
                         .SingleOrDefaultAsync(token);

        var execution = _executionContext.Current;
        var nodeId = execution?.TargetNodeId;

        if (nodeId is null)
        {
            await _queue.EnqueueAsync(
                DeploymentQueueRequest.Vm(gameId, vmInstance.UserId, vmInstance.ChallengeId, vmInstance.Id) with
                {
                    Generation = vmInstance.RuntimeGeneration
                }, token);
            _logger.LogInformation("VM creation queued for instance {VmInstanceId}.", vmInstance.Id);
            return null;
        }

        var node = await _nodeRepo.GetNodeByIdAsync(nodeId.Value, token);
        if (node is null)
        {
            vmInstance.Status = VmInstanceStatus.Error;
            await UpdateTicketStage(execution?.TicketId, DeploymentStage.Failed,
                "Assigned KVM node is no longer available.", token);
            return null;
        }

        var nodeContractError = ValidateCredentialNode(node);
        if (nodeContractError is not null)
        {
            vmInstance.Status = VmInstanceStatus.Error;
            vmInstance.NodeId = nodeId.Value;
            await UpdateTicketStage(execution?.TicketId, DeploymentStage.Failed,
                nodeContractError, token);
            return null;
        }

        var template = templateId.HasValue
            ? await _context.ImageTemplates.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == templateId.Value, token)
            : null;
        var imageContractError = ValidateCredentialImage(template);
        if (imageContractError is not null)
        {
            vmInstance.Status = VmInstanceStatus.Error;
            vmInstance.NodeId = nodeId.Value;
            await UpdateTicketStage(execution?.TicketId, DeploymentStage.Failed,
                imageContractError, token);
            return null;
        }

        _credentialService.Initialize(vmInstance);
        vmInstance.RuntimeGeneration = Math.Max(1, execution?.Generation ?? vmInstance.RuntimeGeneration);
        var rdpPassword = _credentialService.RevealPassword(vmInstance);

        await UpdateTicketStage(execution?.TicketId, DeploymentStage.ImagePreparing,
            "Ensuring VM template on worker from storage registry.", token);
        var imageReady = await EnsureRemoteVmImageAsync(nodeId.Value, node, templateId, token);
        if (!imageReady.Success)
        {
            _logger.LogWarning("Agent VM image ensure failed on node {NodeId}: {Message}",
                nodeId.Value, imageReady.Message);
            vmInstance.Status = VmInstanceStatus.Error;
            vmInstance.NodeId = nodeId.Value;
            await UpdateTicketStage(execution?.TicketId, DeploymentStage.Failed, imageReady.Message, token);
            return null;
        }

        await UpdateTicketStage(execution?.TicketId, DeploymentStage.VmCreating,
            "VM image is ready; creating VM.", token);
        var result = await _agentClient.CreateVmAsync(nodeId.Value, new AgentCreateVmRequest
        {
            Generation = execution?.Generation ?? 1,
            TemplateId = templateId,
            ImageEnsured = true,
            VmName = vmInstance.VmName,
            Memory = memory ?? _kvmSettings.DefaultVmMemoryMb,
            Cpu = cpu ?? _kvmSettings.DefaultVmCpu,
            Flag = flag,
            CloudInit = BuildWindowsCloudInit(vmInstance, rdpPassword)
        }, token);

        if (result is null)
        {
            _logger.LogWarning("Agent VM creation failed on node {NodeId}", nodeId.Value);
            vmInstance.Status = VmInstanceStatus.Error;
            vmInstance.NodeId = nodeId.Value;
            await UpdateTicketStage(execution?.TicketId, DeploymentStage.Failed, "Agent VM creation failed.", token);
            return null;
        }

        vmInstance.Status = VmInstanceStatus.Running;
        vmInstance.NodeId = nodeId.Value;
        vmInstance.RuntimeGeneration = Math.Max(1, result.Generation);
        vmInstance.RuntimeNativeId = string.IsNullOrWhiteSpace(result.NativeId) ? null : result.NativeId;
        await UpdateTicketStage(execution?.TicketId, DeploymentStage.BootProbing,
            "VM started; waiting for readiness probe.", token);
        return vmInstance;
    }

    internal static AgentVmInitConfig BuildWindowsCloudInit(VmInstance vmInstance, string password) =>
        new()
        {
            Enabled = true,
            OsType = OSType.Windows,
            Hostname = vmInstance.VmName,
            InstanceId = $"gzctf-vm-{vmInstance.Id:N}-g{vmInstance.RuntimeGeneration}",
            MetaData =
                $"instance-id: gzctf-vm-{vmInstance.Id:N}-g{vmInstance.RuntimeGeneration}\nlocal-hostname: {vmInstance.VmName}\n",
            UserData = VmCredentialService.BuildWindowsUserData(vmInstance.RdpUsername, password),
            SensitiveKeys = ["user-data", "rdp-password"]
        };

    internal static string? ValidateCredentialNode(WorkerNode node)
    {
        if (node.IsLocal)
            return "Local KVM does not support the required Windows instance credential injection contract.";

        return AgentCapabilityEvaluator.Supports(node, AgentFeatureIds.Kvm, AgentFeatureIds.CloudInit)
            ? null
            : "Assigned KVM node does not advertise Cloud-Init credential injection support.";
    }

    internal static string? ValidateCredentialImage(ImageTemplate? template)
    {
        if (template is null)
            return "Windows VM image template is missing.";
        if (template.OSType != OSType.Windows || template.ImageType == ImageType.Docker)
            return "Assigned image is not a Windows VM image.";
        if (template.Status != ImageStatus.Ready)
            return "Windows VM image is not ready.";
        return template.SupportsInstanceCredentials
            ? null
            : "Windows image is not verified for instance-specific Cloudbase-Init credentials.";
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

    async Task UpdateTicketStage(Guid? ticketId, DeploymentStage stage, string message, CancellationToken token)
    {
        if (ticketId is null)
            return;
        var ticket = await _context.DeploymentQueueTickets.FirstOrDefaultAsync(item => item.Id == ticketId, token);
        if (ticket is null)
            return;
        ticket.Stage = stage;
        ticket.StageMessage = message;
        if (stage == DeploymentStage.Failed)
            ticket.ErrorMessage = message;
        await _context.SaveChangesAsync(token);
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
                await _agentClient.DestroyVmAsync(
                    vmInstance.NodeId!.Value,
                    vmInstance.VmName,
                    vmInstance.RuntimeGeneration,
                    vmInstance.RuntimeNativeId,
                    token);
            }
            catch (Exception ex)
            {
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

        if (hadCapacityReservation && node is not null)
        {
            node.CurrentVms = Math.Max(0, node.CurrentVms - 1);
            await _context.SaveChangesAsync(token);
        }

        _logger.SystemLog($"Destroyed VM {vmInstance.VmName}.", TaskStatus.Success, LogLevel.Information);
    }

}
