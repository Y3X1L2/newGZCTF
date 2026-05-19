namespace GZCTF.Services.Vm;

/// <summary>
/// Result of a VM operation. Success=true when the operation completed without error.
/// </summary>
public class VmOperationResult
{
    public bool Success { get; init; }
    public string VmName { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    public static VmOperationResult Ok(string vmName) => new() { Success = true, VmName = vmName };
    public static VmOperationResult Fail(string vmName, string error) => new()
        { Success = false, VmName = vmName, ErrorMessage = error };
}
