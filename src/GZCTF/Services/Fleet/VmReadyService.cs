using GZCTF.Models.Data;
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

        // Find VMs that are Running but don't have an IP or Guacamole connection yet
        var pendingVms = await dbContext.VmInstances
            .Where(v => v.Status == VmInstanceStatus.Running
                        && (v.IpAddress == null || v.GuacamoleConnectionId == null))
            .ToListAsync(token);

        if (pendingVms.Count == 0) return;

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
}
