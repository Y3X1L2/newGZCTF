using GZCTF.Models.Data;
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
                    vm.Status = VmInstanceStatus.Error;
                    await dbContext.SaveChangesAsync(token);
                    continue;
                }

                // Step 1: Get IP if not yet available
                if (string.IsNullOrEmpty(vm.IpAddress))
                {
                    var ip = await vmProvider.GetIpAddressAsync(vm.VmName, token);
                    if (string.IsNullOrEmpty(ip))
                    {
                        _logger.LogDebug("VM {VmName}: IP not yet available, will retry", vm.VmName);
                        continue;
                    }

                    vm.IpAddress = ip;
                    await dbContext.SaveChangesAsync(token);
                    _logger.LogInformation("VM {VmName}: got IP {Ip}", vm.VmName, ip);
                }

                // Step 2: Create Guacamole RDP connection if not yet created
                if (string.IsNullOrEmpty(vm.GuacamoleConnectionId))
                {
                    var connectionId = await guacService.CreateRdpConnectionAsync(
                        connectionName: vm.VmName,
                        vmIp: vm.IpAddress!,
                        rdpPort: 3389,
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
}
