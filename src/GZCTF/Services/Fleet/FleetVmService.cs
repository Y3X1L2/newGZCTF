using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Vm;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public class FleetVmService
{
    private readonly FleetManager _fleetManager;
    private readonly AgentClient _agentClient;
    private readonly INodeRepository _nodeRepo;
    private readonly IVirtualMachineProvider _vmProvider;
    private readonly KvmSettings _kvmSettings;
    private readonly ILogger<FleetVmService> _logger;

    public FleetVmService(
        FleetManager fleetManager,
        AgentClient agentClient,
        INodeRepository nodeRepo,
        IVirtualMachineProvider vmProvider,
        IOptions<KvmSettings> kvmSettings,
        ILogger<FleetVmService> logger)
    {
        _fleetManager = fleetManager;
        _agentClient = agentClient;
        _nodeRepo = nodeRepo;
        _vmProvider = vmProvider;
        _kvmSettings = kvmSettings.Value;
        _logger = logger;
    }

    public async Task<VmInstance?> CreateVmAsync(VmInstance vmInstance, int? templateId, string? templatePath,
        int? memory, int? cpu, CancellationToken token)
    {
        var target = new DeploymentTarget
        {
            Type = TargetType.Vm,
            Action = TargetAction.Create,
            Payload = JsonSerializer.Serialize(new VmCreatePayload(
                templateId, templatePath, memory, cpu, vmInstance.VmName))
        };
        var nodeId = await _fleetManager.TryScheduleAsync(target, token);
        if (nodeId is null)
        {
            _logger.LogWarning("No KVM node available, VM creation queued");
            return null;
        }

        var node = await _nodeRepo.GetNodeByIdAsync(nodeId.Value, token);
        if (node?.IsLocal == true)
        {
            return await CreateLocalVmAsync(vmInstance, templatePath, token);
        }

        var result = await _agentClient.CreateVmAsync(nodeId.Value, new AgentCreateVmRequest
        {
            TemplateId = templateId,
            VmName = vmInstance.VmName,
            Memory = memory ?? _kvmSettings.DefaultVmMemoryMb,
            Cpu = cpu ?? _kvmSettings.DefaultVmCpu
        }, token);

        if (result is null)
        {
            _logger.LogWarning("Agent VM creation failed on node {NodeId}", nodeId.Value);
            return null;
        }

        vmInstance.Status = VmInstanceStatus.Running;
        vmInstance.NodeId = nodeId.Value;
        return vmInstance;
    }

    private async Task<VmInstance?> CreateLocalVmAsync(VmInstance vmInstance, string? templatePath,
        CancellationToken token)
    {
        if (string.IsNullOrEmpty(templatePath))
        {
            _logger.LogError("No template path provided for local VM creation");
            vmInstance.Status = VmInstanceStatus.Error;
            return vmInstance;
        }

        try
        {
            var createResult = await _vmProvider.CreateFromTemplateAsync(templatePath, vmInstance.VmName, token);
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
        if (!vmInstance.NodeId.HasValue)
        {
            vmInstance.Status = VmInstanceStatus.Destroyed;
            vmInstance.DestroyedAt = DateTimeOffset.UtcNow;
            return;
        }

        var node = await _nodeRepo.GetNodeByIdAsync(vmInstance.NodeId.Value, token);
        if (node?.IsLocal == true)
        {
            try
            {
                await _vmProvider.DestroyAsync(vmInstance.VmName, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local VM destruction failed for {VmName}", vmInstance.VmName);
            }
        }
        else
        {
            try
            {
                await _agentClient.DestroyVmAsync(vmInstance.NodeId.Value, vmInstance.VmName, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent VM destruction failed for {VmName}", vmInstance.VmName);
            }
        }

        vmInstance.Status = VmInstanceStatus.Destroyed;
        vmInstance.DestroyedAt = DateTimeOffset.UtcNow;
    }

    private sealed record VmCreatePayload(
        int? TemplateId, string? TemplatePath, int? Memory, int? Cpu, string VmName);
}
