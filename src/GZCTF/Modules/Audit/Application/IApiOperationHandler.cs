namespace GZCTF.Modules.Audit.Application;

public interface IApiOperationHandler
{
    string Kind { get; }

    /// <summary>
    /// Executes an at-least-once durable operation. Implementations must load their durable job by
    /// <paramref name="operationId"/> and make repeated execution idempotent before creating resources.
    /// </summary>
    Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken cancellationToken);

    /// <summary>
    /// Releases durable staging artifacts after the operation reaches a terminal failed state.
    /// Implementations must be idempotent because recovery may invoke cleanup more than once.
    /// </summary>
    Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
