using GZCTF.GuestControl.Contracts;

namespace GZCTF.Agent.Services.GuestControl;

public sealed class GuestEventIngestor(GuestEnrollmentStore store)
{
    public Task<GuestEventDisposition> IngestAsync(string certificateThumbprint,
        GuestLifecycleEvent guestEvent, CancellationToken cancellationToken) =>
        store.AcceptEventAsync(certificateThumbprint, guestEvent, cancellationToken);
}
