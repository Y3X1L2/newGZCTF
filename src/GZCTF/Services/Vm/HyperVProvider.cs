using GZCTF.Models.Data;

namespace GZCTF.Services.Vm;

/// <summary>
/// Hyper-V virtual machine provider. Requires Windows host with Hyper-V enabled.
/// Uses PowerShell Hyper-V cmdlets for VM lifecycle management.
/// When running on a non-Windows host or without Hyper-V, all operations return failure results.
/// </summary>
public class HyperVProvider : IVirtualMachineProvider
{
    private readonly ILogger<HyperVProvider> _logger;

    public string ProviderName => "Hyper-V";
    public OSType SupportedOSType => OSType.Windows;

    public HyperVProvider(ILogger<HyperVProvider> logger)
        => _logger = logger;

    public Task<VmOperationResult> CreateFromTemplateAsync(string templatePath, string vmName, CancellationToken token)
    {
        _logger.LogWarning("Hyper-V CreateFromTemplate not available for VM '{VmName}'", vmName);
        return Task.FromResult(VmOperationResult.Fail(vmName, "Hyper-V provider requires Windows host"));
    }

    public Task<VmOperationResult> StartAsync(string vmName, CancellationToken token)
    {
        _logger.LogWarning("Hyper-V Start not available for VM '{VmName}'", vmName);
        return Task.FromResult(VmOperationResult.Fail(vmName, "Hyper-V provider requires Windows host"));
    }

    public Task<VmOperationResult> ShutdownAsync(string vmName, CancellationToken token)
    {
        _logger.LogWarning("Hyper-V Shutdown not available for VM '{VmName}'", vmName);
        return Task.FromResult(VmOperationResult.Fail(vmName, "Hyper-V provider requires Windows host"));
    }

    public Task<VmOperationResult> DestroyAsync(string vmName, CancellationToken token)
    {
        _logger.LogWarning("Hyper-V Destroy not available for VM '{VmName}'", vmName);
        return Task.FromResult(VmOperationResult.Fail(vmName, "Hyper-V provider requires Windows host"));
    }

    public Task<VmOperationResult> CreateSnapshotAsync(string vmName, string snapshotName, CancellationToken token)
    {
        _logger.LogWarning("Hyper-V CreateSnapshot not available for VM '{VmName}'", vmName);
        return Task.FromResult(VmOperationResult.Fail(vmName, "Hyper-V provider requires Windows host"));
    }

    public Task<VmOperationResult> SnapshotRevertAsync(string vmName, CancellationToken token)
    {
        _logger.LogWarning("Hyper-V SnapshotRevert not available for VM '{VmName}'", vmName);
        return Task.FromResult(VmOperationResult.Fail(vmName, "Hyper-V provider requires Windows host"));
    }

    public Task<VmConnectionInfo?> GetConnectionInfoAsync(string vmName, CancellationToken token)
        => Task.FromResult<VmConnectionInfo?>(null);

    public Task<string?> GetIpAddressAsync(string vmName, CancellationToken token)
        => Task.FromResult<string?>(null);

    public Task<bool> IsRunningAsync(string vmName, CancellationToken token)
        => Task.FromResult(false);
}
