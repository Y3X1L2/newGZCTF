using GZCTF.Models.Data;

namespace GZCTF.Services.Vm;

/// <summary>
/// Abstraction over hypervisor-specific VM lifecycle operations.
/// Implementations: KvmProvider (Linux KVM/libvirt), HyperVProvider (Windows Hyper-V).
/// </summary>
public interface IVirtualMachineProvider
{
    string ProviderName { get; }
    OSType SupportedOSType { get; }

    Task<VmOperationResult> CreateFromTemplateAsync(string templatePath, string vmName, int? memoryMb = null, int? cpuCount = null, CancellationToken token = default);
    Task<VmOperationResult> StartAsync(string vmName, CancellationToken token);
    Task<VmOperationResult> ShutdownAsync(string vmName, CancellationToken token);
    Task<VmOperationResult> DestroyAsync(string vmName, CancellationToken token);
    Task<VmOperationResult> CreateSnapshotAsync(string vmName, string snapshotName, CancellationToken token);
    Task<VmOperationResult> SnapshotRevertAsync(string vmName, CancellationToken token);
    Task<VmConnectionInfo?> GetConnectionInfoAsync(string vmName, CancellationToken token);
    Task<string?> GetIpAddressAsync(string vmName, CancellationToken token);
    Task<bool> IsRunningAsync(string vmName, CancellationToken token);
}
