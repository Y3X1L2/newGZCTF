namespace GZCTF.Modules.Identity.Contracts;

/// <summary>Module-owned access revocation when an account is retired.</summary>
public interface IUserAccountRevocationParticipant
{
    /// <summary>
    /// Persist access denial in the caller's database transaction, without external I/O.
    /// Implementations use the scoped database context and must support later retry.
    /// </summary>
    Task PrepareAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Close external channels after commit. Failure must leave durable retryable cleanup state.
    /// </summary>
    Task CompleteAsync(Guid userId, CancellationToken cancellationToken);
}
