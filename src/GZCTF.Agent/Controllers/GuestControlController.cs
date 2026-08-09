using GZCTF.Agent.Models;
using GZCTF.Agent.Services.GuestControl;
using GZCTF.Agent.Services.Vm;
using GZCTF.GuestControl.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/guest-control")]
public sealed class GuestControlController(
    GuestEnrollmentStore store,
    GuestCertificateAuthority certificateAuthority,
    GuestManagementNetworkService network,
    IOptions<AgentConfig> options) : ControllerBase
{
    [HttpGet("network")]
    public async Task<GuestManagementEndpointInfo> Network(CancellationToken cancellationToken)
    {
        var config = options.Value.GuestManagement;
        return new GuestManagementEndpointInfo(
            config.BridgeName,
            config.HostAddress,
            config.PrefixLength,
            config.ListenPort,
            await network.IsHealthyAsync(cancellationToken));
    }
    [HttpPost("network/apply")]
    public Task<TeamLabDryRunResponse> ApplyNetwork(
        [FromQuery] bool dryRun,
        CancellationToken cancellationToken) =>
        network.ApplyAsync(dryRun, cancellationToken);

    [HttpPost("prepare")]
    public async Task<GuestControlPrepareResponse> Prepare(
        GuestControlPrepareRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Identity.NativeVmId != VmDomainBuilder.BuildStableDomainId(
                request.Identity.VmName, request.Identity.Generation))
            throw new ArgumentException("guest_native_vm_identity_invalid", nameof(request));
        if (!await network.IsHealthyAsync(cancellationToken))
            throw new InvalidOperationException("guest_management_network_not_ready");
        return await store.PrepareAsync(
            request, certificateAuthority.GetServerCertificateSha256(), cancellationToken);
    }

    [HttpPost("status")]
    public async Task<ActionResult<GuestControlStatus>> Status(
        GuestAssetIdentity identity,
        CancellationToken cancellationToken)
    {
        var status = await store.GetStatusAsync(identity, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(
        GuestAssetIdentity identity,
        CancellationToken cancellationToken) =>
        await store.RevokeAsync(identity, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("conformance-package")]
    public async Task<IActionResult> StageConformancePackage(
        StageGuestConformancePackageRequest request,
        CancellationToken cancellationToken)
    {
        var digest = request.ArtifactDigest.Trim().ToLowerInvariant();
        if (digest.StartsWith("sha256:", StringComparison.Ordinal)) digest = digest[7..];
        if (request.ProfileId == Guid.Empty || request.Version <= 0 || digest.Length != 64 ||
            !digest.All(Uri.IsHexDigit))
            throw new ArgumentException("guest_conformance_package_invalid", nameof(request));
        byte[] payload;
        try { payload = Convert.FromBase64String(request.ArtifactBase64); }
        catch (FormatException) { throw new ArgumentException("guest_conformance_package_invalid", nameof(request)); }
        if (!string.Equals(
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload)),
                digest,
                StringComparison.Ordinal))
            throw new InvalidDataException("guest_conformance_package_digest_mismatch");
        var directory = Path.Combine(
            "/var/lib/gzctf/bootstrap-profiles",
            request.ProfileId.ToString("N"),
            request.Version.ToString());
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{digest}.tar.gz");
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        await System.IO.File.WriteAllBytesAsync(temporary, payload, cancellationToken);
        System.IO.File.Move(temporary, destination, true);
        if (!OperatingSystem.IsWindows())
            System.IO.File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return NoContent();
    }
}
