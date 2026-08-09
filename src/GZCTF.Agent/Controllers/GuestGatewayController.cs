using GZCTF.Agent.Services.GuestControl;
using GZCTF.GuestControl.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/guest/v1")]
public sealed class GuestGatewayController(
    GuestEnrollmentStore store,
    GuestCertificateAuthority certificateAuthority,
    GuestEventIngestor eventIngestor) : ControllerBase
{
    [HttpPost("enroll")]
    public async Task<GuestEnrollmentSessionResponse> Enroll(
        GuestEnrollmentEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completion = await store.EnrollAsync(
            envelope,
            certificateAuthority.IssueClientCertificate,
            cancellationToken);
        return new GuestEnrollmentSessionResponse(
            completion.Response,
            completion.Intent,
            completion.ManagementLease);
    }

    [HttpPost("events")]
    public async Task<ActionResult<GuestEventDisposition>> Event(
        GuestEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var certificate = await HttpContext.Connection.GetClientCertificateAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("guest_client_certificate_required");
        if (!certificateAuthority.IsIssuedClientCertificate(certificate))
            throw new UnauthorizedAccessException("guest_client_certificate_invalid");
        return Ok(await eventIngestor.IngestAsync(
            certificate.Thumbprint,
            envelope.Event,
            cancellationToken));
    }

    [HttpPost("artifacts")]
    public async Task<IActionResult> Artifact(
        GuestArtifactRequest request,
        CancellationToken cancellationToken)
    {
        var certificate = await RequireClientCertificateAsync(cancellationToken);
        var intent = await store.GetIntentAsync(certificate.Thumbprint, request.Identity, cancellationToken);
        var descriptor = intent.ServicePackage;
        if (descriptor is null || descriptor.ProfileId != request.ProfileId ||
            descriptor.Version != request.Version ||
            !string.Equals(descriptor.ArtifactDigest, request.ArtifactDigest, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("guest_artifact_not_authorized");
        var digest = request.ArtifactDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? request.ArtifactDigest[7..]
            : request.ArtifactDigest;
        if (digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("guest_artifact_digest_invalid", nameof(request));
        var path = Path.Combine(
            "/var/lib/gzctf/bootstrap-profiles",
            request.ProfileId.ToString("N"),
            request.Version.ToString(),
            $"{digest.ToLowerInvariant()}.tar.gz");
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/gzip", enableRangeProcessing: true);
    }

    [HttpPost("secrets")]
    public async Task<ActionResult<GuestSecretResponse>> Secrets(
        GuestSecretRequest request,
        CancellationToken cancellationToken)
    {
        var certificate = await RequireClientCertificateAsync(cancellationToken);
        return Ok(await store.GetSecretsAsync(
            certificate.Thumbprint, request, cancellationToken));
    }

    private async Task<System.Security.Cryptography.X509Certificates.X509Certificate2>
        RequireClientCertificateAsync(CancellationToken cancellationToken)
    {
        var certificate = await HttpContext.Connection.GetClientCertificateAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("guest_client_certificate_required");
        if (!certificateAuthority.IsIssuedClientCertificate(certificate))
            throw new UnauthorizedAccessException("guest_client_certificate_invalid");
        return certificate;
    }
}
