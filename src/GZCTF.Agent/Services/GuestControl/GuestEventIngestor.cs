using GZCTF.Agent.Models;
using GZCTF.Agent.Services.RuntimeSignals;
using GZCTF.GuestControl.Contracts;

namespace GZCTF.Agent.Services.GuestControl;

public sealed class GuestEventIngestor(
    GuestEnrollmentStore store,
    AgentRuntimeSignalJournal journal,
    AgentRuntimeSignalPublisher publisher)
{
    public Task<GuestEventDisposition> IngestAsync(
        string certificateThumbprint,
        GuestLifecycleEvent guestEvent,
        CancellationToken cancellationToken) =>
        store.AcceptEventAsync(
            certificateThumbprint,
            guestEvent,
            token => JournalOnceAsync(guestEvent, token),
            cancellationToken);

    private async Task JournalOnceAsync(
        GuestLifecycleEvent guestEvent,
        CancellationToken cancellationToken)
    {
        var guestSequence = guestEvent.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var existing = await journal.ReadAllAsync(guestEvent.Identity.OperationId, cancellationToken);
        if (existing.Any(item => item.Facts is not null &&
                                 item.Facts.TryGetValue("guestSequence", out var sequence) &&
                                 string.Equals(sequence, guestSequence, StringComparison.Ordinal)))
            return;

        var facts = new Dictionary<string, string>(guestEvent.Facts ?? new Dictionary<string, string>(),
            StringComparer.Ordinal)
        {
            ["guestSequence"] = guestSequence,
            ["nativeVmId"] = guestEvent.Identity.NativeVmId.ToString("D"),
            ["bootEpoch"] = guestEvent.Identity.BootEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["payloadDigest"] = guestEvent.PayloadDigest
        };
        await publisher.AppendAsync(new AgentRuntimeSignalDraft(
            guestEvent.Identity.OperationId,
            guestEvent.Identity.RuntimeId,
            guestEvent.Identity.Generation,
            "vm-guest",
            guestEvent.Identity.VmName,
            guestEvent.Stage.ToRuntimeSignalStage(),
            guestEvent.Outcome switch
            {
                GuestLifecycleOutcome.Started => AgentRuntimeSignalOutcome.Started,
                GuestLifecycleOutcome.Ready => AgentRuntimeSignalOutcome.Ready,
                GuestLifecycleOutcome.Failed => AgentRuntimeSignalOutcome.Failed,
                _ => throw new ArgumentOutOfRangeException(nameof(guestEvent))
            },
            guestEvent.ErrorCode,
            false,
            facts), cancellationToken);
    }
}
