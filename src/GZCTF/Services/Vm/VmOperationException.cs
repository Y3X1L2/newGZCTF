namespace GZCTF.Services.Vm;

/// <summary>
/// Exception thrown when a VM operation fails.
/// Wraps underlying hypervisor errors for consistent error handling upstream.
/// </summary>
public class VmOperationException : Exception
{
    /// <summary>
    /// Creates a new <see cref="VmOperationException"/> with the specified error message.
    /// </summary>
    public VmOperationException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="VmOperationException"/> with the specified error message and inner exception.
    /// </summary>
    public VmOperationException(string message, Exception inner) : base(message, inner) { }
}
