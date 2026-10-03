using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.Vm;

/// <summary>The QGA control channel is independent of the guest's business network.</summary>
public interface IVmGuestAgentClient
{
    Task<VmGuestStatusResponse> WaitReadyAsync(string vmName, TimeSpan timeout, CancellationToken cancellationToken);
    /// <summary>Read-only optional tool probe; absent files and unsupported file RPCs return false.</summary>
    Task<bool> TryFileExistsAsync(string vmName, string guestPath, CancellationToken cancellationToken);
    Task<VmGuestCommandResponse> ExecuteAsync(string vmName, VmGuestCommandRequest command,
        CancellationToken cancellationToken, Func<CancellationToken, Task<bool>>? verifyIdentity = null);
}
