using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Vm;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

/// <summary>
/// Background service that polls newly-created VMs for IP addresses,
/// then creates Guacamole RDP connections once the VM is reachable.
/// </summary>
public class VmReadyService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VmReadyService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxWaitTime = TimeSpan.FromMinutes(10);

    public VmReadyService(
        IServiceScopeFactory scopeFactory,
        ILogger<VmReadyService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for the app to fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingVmsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in VmReadyService poll cycle");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingVmsAsync(CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vmProvider = scope.ServiceProvider.GetRequiredService<IVirtualMachineProvider>();
        var guacService = scope.ServiceProvider.GetRequiredService<GuacamoleService>();
        var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
        var agentClient = scope.ServiceProvider.GetRequiredService<AgentClient>();
        var events = scope.ServiceProvider.GetRequiredService<IOperationalEventWriter>();

        // Find VMs that are Running but don't have an IP or Guacamole connection yet
        var pendingVms = await dbContext.VmInstances
            .Where(v => v.Status == VmInstanceStatus.Running
                        && (v.IpAddress == null || v.GuacamoleConnectionId == null))
            .ToListAsync(token);

        if (pendingVms.Count == 0) return;

        var vmIds = pendingVms.Select(vm => vm.Id).ToArray();
        var existingCodes = await dbContext.OperationalEvents.AsNoTracking()
            .Where(item => item.VmInstanceId != null && vmIds.Contains(item.VmInstanceId.Value) &&
                           (item.EventCode == OperationalEventCodes.Vm.BootProbeStarted ||
                            item.EventCode == OperationalEventCodes.Vm.BootReady ||
                            item.EventCode == OperationalEventCodes.Vm.AccessOpened))
            .Select(item => new { VmId = item.VmInstanceId!.Value, item.EventCode })
            .ToArrayAsync(token);
        var eventSet = existingCodes.Select(item => (item.VmId, item.EventCode)).ToHashSet();
        var ticketIds = await dbContext.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.VmInstanceId != null && vmIds.Contains(ticket.VmInstanceId.Value))
            .OrderByDescending(ticket => ticket.CreatedAt)
            .Select(ticket => new { VmId = ticket.VmInstanceId!.Value, ticket.Id })
            .ToArrayAsync(token);
        var correlations = ticketIds.GroupBy(item => item.VmId)
            .ToDictionary(group => group.Key, group => group.First().Id);
        foreach (var vm in pendingVms.Where(vm =>
                     !eventSet.Contains((vm.Id, OperationalEventCodes.Vm.BootProbeStarted))))
            events.Append(VmEvent(
                vm,
                OperationalEventCodes.Vm.BootProbeStarted,
                OperationalEventOutcome.Started,
                "VM boot readiness probing started.",
                correlations.GetValueOrDefault(vm.Id)));
        await dbContext.SaveChangesAsync(token);

        _logger.LogInformation("VmReadyService: checking {Count} pending VM(s)", pendingVms.Count);

        foreach (var vm in pendingVms)
        {
            try
            {
                // Check if VM has been waiting too long
                if (DateTimeOffset.UtcNow - vm.CreatedAt > MaxWaitTime)
                {
                    _logger.LogWarning("VM {VmName} timed out waiting for IP (created {Ago} ago)",
                        vm.VmName, DateTimeOffset.UtcNow - vm.CreatedAt);
                    var fleetVm = scope.ServiceProvider.GetRequiredService<FleetVmService>();
                    try
                    {
                        await fleetVm.DestroyVmAsync(vm, token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "VM {VmName} timed out and automatic destruction failed; marking as Error.",
                            vm.VmName);
                    }

                    vm.Status = VmInstanceStatus.Error;
                    vm.DestroyedAt ??= DateTimeOffset.UtcNow;
                    var error = new OperationalError(
                        OperationalErrorCategory.HealthCheck,
                        OperationalErrorCodes.HealthProbeTimeout,
                        "VM boot readiness probe timed out.",
                        false,
                        WorkerNodeId: vm.NodeId,
                        Operation: "vm.boot.probe");
                    events.Append(VmEvent(
                        vm,
                        OperationalEventCodes.Vm.BootFailed,
                        OperationalEventOutcome.Failed,
                        "VM boot readiness probe timed out.",
                        correlations.GetValueOrDefault(vm.Id),
                        error));
                    if (string.IsNullOrWhiteSpace(vm.GuacamoleConnectionId))
                        events.Append(VmEvent(
                            vm,
                            OperationalEventCodes.Vm.AccessFailed,
                            OperationalEventOutcome.Failed,
                            "VM remote access did not become ready before timeout.",
                            correlations.GetValueOrDefault(vm.Id),
                            error));
                    await dbContext.SaveChangesAsync(token);
                    continue;
                }

                var node = vm.NodeId.HasValue
                    ? await nodeRepo.GetNodeByIdAsync(vm.NodeId.Value, token)
                    : null;
                VmAccessEndpoint? accessEndpoint = null;

                // Step 1: Get IP if not yet available
                if (string.IsNullOrEmpty(vm.IpAddress))
                {
                    accessEndpoint = await GetVmAccessEndpointAsync(vm, node, agentClient, vmProvider, token);
                    if (string.IsNullOrEmpty(accessEndpoint?.IpAddress))
                    {
                        _logger.LogDebug("VM {VmName}: IP not yet available, will retry", vm.VmName);
                        continue;
                    }

                    vm.IpAddress = accessEndpoint.IpAddress;
                    if (!eventSet.Contains((vm.Id, OperationalEventCodes.Vm.BootReady)))
                    {
                        events.Append(VmEvent(
                            vm,
                            OperationalEventCodes.Vm.BootReady,
                            OperationalEventOutcome.Succeeded,
                            "VM boot readiness probe completed.",
                            correlations.GetValueOrDefault(vm.Id)));
                        eventSet.Add((vm.Id, OperationalEventCodes.Vm.BootReady));
                    }
                    await dbContext.SaveChangesAsync(token);
                    _logger.LogInformation("VM {VmName}: got IP {Ip}", vm.VmName, accessEndpoint.IpAddress);
                }

                // Step 2: Create Guacamole RDP connection if not yet created
                if (string.IsNullOrEmpty(vm.GuacamoleConnectionId))
                {
                    accessEndpoint ??= await GetVmAccessEndpointAsync(vm, node, agentClient, vmProvider, token);
                    if (accessEndpoint is null)
                    {
                        _logger.LogDebug("VM {VmName}: RDP endpoint not yet available, will retry", vm.VmName);
                        continue;
                    }

                    var connectionId = await guacService.CreateRdpConnectionAsync(
                        connectionName: vm.VmName,
                        vmIp: accessEndpoint.RdpHost,
                        rdpPort: accessEndpoint.RdpPort,
                        username: vm.RdpUsername,
                        password: vm.RdpPassword,
                        token: token);

                    if (string.IsNullOrEmpty(connectionId))
                    {
                        _logger.LogWarning("VM {VmName}: failed to create Guacamole connection, will retry",
                            vm.VmName);
                        continue;
                    }

                    vm.GuacamoleConnectionId = connectionId;
                    vm.RdpUrl = guacService.GetConnectionUrl(connectionId);
                    if (!eventSet.Contains((vm.Id, OperationalEventCodes.Vm.AccessOpened)))
                    {
                        events.Append(VmEvent(
                            vm,
                            OperationalEventCodes.Vm.AccessOpened,
                            OperationalEventOutcome.Succeeded,
                            "VM remote access connection opened.",
                            correlations.GetValueOrDefault(vm.Id)));
                        eventSet.Add((vm.Id, OperationalEventCodes.Vm.AccessOpened));
                    }
                    await dbContext.SaveChangesAsync(token);

                    _logger.LogInformation(
                        "VM {VmName}: Guacamole RDP connection ready (ID: {ConnId}, URL: {Url})",
                        vm.VmName, connectionId, vm.RdpUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing VM {VmName}", vm.VmName);
            }
        }
    }

    private async Task<VmAccessEndpoint?> GetVmAccessEndpointAsync(
        VmInstance vm,
        WorkerNode? node,
        AgentClient agentClient,
        IVirtualMachineProvider vmProvider,
        CancellationToken token)
    {
        if (node is null || node.IsLocal)
        {
            var ip = vm.IpAddress ?? await vmProvider.GetIpAddressAsync(vm.VmName, token);
            return string.IsNullOrEmpty(ip)
                ? null
                : new VmAccessEndpoint(ip, ip, 3389);
        }

        var response = await agentClient.GetVmIpAsync(node.Id, vm.VmName, token);
        if (string.IsNullOrEmpty(response?.IpAddress))
            return null;

        return new VmAccessEndpoint(response.IpAddress, node.HostAddress, response.RdpPort ?? 3389);
    }

    private sealed record VmAccessEndpoint(string IpAddress, string RdpHost, int RdpPort);

    private static OperationalEventDraft VmEvent(
        VmInstance vm,
        string eventCode,
        OperationalEventOutcome outcome,
        string message,
        Guid? correlationId,
        OperationalError? error = null) =>
        new(
            eventCode,
            outcome,
            message,
            outcome == OperationalEventOutcome.Failed
                ? OperationalEventSeverity.Error
                : OperationalEventSeverity.Information,
            correlationId ?? vm.Id,
            error?.Category,
            error?.Code,
            error?.Retryable ?? false,
            new Dictionary<string, object?>
            {
                ["operation"] = "vm.boot.probe",
                ["stage"] = vm.Status.ToString()
            },
            OwnerUserId: vm.UserId,
            ChallengeId: vm.ChallengeId,
            WorkerNodeId: vm.NodeId,
            VmInstanceId: vm.Id,
            SubjectType: "vm-instance",
            SubjectId: vm.Id.ToString(),
            SubjectDisplayName: vm.VmName,
            ResourceType: "vm-instance",
            ResourceId: vm.Id.ToString(),
            ResourceDisplayName: vm.VmName);
}
