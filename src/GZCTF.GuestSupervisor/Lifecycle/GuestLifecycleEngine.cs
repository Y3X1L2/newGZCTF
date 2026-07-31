using GZCTF.GuestControl.Contracts;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed class GuestLifecycleEngine(GuestCheckpointStore store)
{
    public async Task<GuestLocalCheckpoint> AdvanceAsync(
        GuestLocalCheckpoint current,
        GuestLifecycleStage? expected,
        GuestLifecycleStage next,
        string payloadDigest,
        CancellationToken cancellationToken,
        GuestLifecycleOutcome outcome = GuestLifecycleOutcome.Ready,
        string? errorCode = null)
    {
        if (current.Stage != expected)
            throw new InvalidOperationException("guest_lifecycle_compare_exchange_failed");
        GuestControlContractValidator.ValidateLifecycleTransition(expected, next);
        var updated = current with
        {
            Stage = next,
            Sequence = checked(current.Sequence + 1),
            PayloadDigest = payloadDigest,
            EmissionAcknowledged = false,
            Outcome = outcome,
            ErrorCode = errorCode,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<GuestLocalCheckpoint> MarkEmissionAcknowledgedAsync(
        GuestLocalCheckpoint current,
        CancellationToken cancellationToken)
    {
        if (current.Stage is null || current.EmissionAcknowledged) return current;
        var updated = current with
        {
            EmissionAcknowledged = true,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(updated, cancellationToken);
        return updated;
    }

}
