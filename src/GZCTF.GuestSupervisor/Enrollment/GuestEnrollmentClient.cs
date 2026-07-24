using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GZCTF.GuestControl.Contracts;
using GZCTF.GuestSupervisor.Lifecycle;

namespace GZCTF.GuestSupervisor.Enrollment;

public interface IGuestGatewayClient
{
    Task<string> DownloadArtifactAsync(
        GuestServicePackageDescriptor descriptor,
        GuestAssetIdentity identity,
        CancellationToken cancellationToken);

    Task<GuestSecretResponse> FetchSecretsAsync(
        GuestAssetIdentity identity,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken);
}

public sealed class GuestEnrollmentClient(
    GuestSupervisorConfiguration configuration)
    : IGuestGatewayClient
{
    private readonly string _keyPath = Path.Combine(configuration.StateRoot, "client-key.pem");
    private readonly string _certificatePath = Path.Combine(configuration.StateRoot, "client.pfx");

    public async Task<GuestEnrollmentSessionResponse?> EnsureEnrolledAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_certificatePath))
            return null;
        Directory.CreateDirectory(configuration.StateRoot);
        using var key = LoadOrCreateKey();
        var request = new CertificateRequest(
            $"CN={configuration.Identity.AssetKey}", key, HashAlgorithmName.SHA256);
        var envelope = new GuestEnrollmentEnvelope(
            configuration.EnrollmentToken,
            new GuestEnrollmentRequest(
                GuestControlProtocol.SchemaVersion,
                configuration.Identity,
                request.CreateSigningRequestPem(),
                GuestControlProtocol.CsrAlgorithm,
                configuration.IntentDigest,
                DateTimeOffset.UtcNow));
        using var handler = CreatePinnedHandler(null);
        using var client = new HttpClient(handler);
        using var response = await client.PostAsJsonAsync(
            configuration.EnrollmentEndpoint, envelope, cancellationToken);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<GuestEnrollmentSessionResponse>(
            cancellationToken)
            ?? throw new InvalidDataException("guest_enrollment_response_invalid");
        using var certificate = X509Certificate2.CreateFromPem(
            session.Enrollment.ClientCertificatePem,
            key.ExportPkcs8PrivateKeyPem());
        await File.WriteAllBytesAsync(_certificatePath,
            certificate.Export(X509ContentType.Pkcs12), cancellationToken);
        Restrict(_certificatePath);
        return session;
    }

    public X509Certificate2 LoadClientCertificate() =>
        X509CertificateLoader.LoadPkcs12FromFile(
            _certificatePath, null,
            OperatingSystem.IsWindows()
                ? X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet
                : X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

    public async Task PublishEventAsync(
        GuestLifecycleEvent guestEvent,
        CancellationToken cancellationToken)
    {
        using var certificate = LoadClientCertificate();
        using var handler = CreatePinnedHandler(certificate);
        using var client = new HttpClient(handler);
        var endpoint = new Uri(configuration.EnrollmentEndpoint, "events");
        using var response = await client.PostAsJsonAsync(
            endpoint, new GuestEventEnvelope(guestEvent), cancellationToken);
        response.EnsureSuccessStatusCode();
        var disposition = await response.Content.ReadFromJsonAsync<GuestEventDisposition>(cancellationToken);
        if (disposition is not GuestEventDisposition.Accepted and not GuestEventDisposition.Duplicate)
            throw new InvalidOperationException("guest_event_rejected");
    }

    public async Task<string> DownloadArtifactAsync(
        GuestServicePackageDescriptor descriptor,
        GuestAssetIdentity identity,
        CancellationToken cancellationToken)
    {
        var destination = Path.Combine(
            configuration.StateRoot,
            "artifacts",
            $"{NormalizeDigest(descriptor.ArtifactDigest)}.tar.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && await VerifyFileAsync(
                destination, descriptor.ArtifactDigest, descriptor.ArtifactSize, cancellationToken))
            return destination;
        var temporary = destination + ".part";
        if (File.Exists(temporary)) File.Delete(temporary);
        using var certificate = LoadClientCertificate();
        using var handler = CreatePinnedHandler(certificate);
        using var client = new HttpClient(handler);
        using var response = await client.PostAsJsonAsync(
            descriptor.ArtifactEndpoint,
            new GuestArtifactRequest(
                identity,
                descriptor.ProfileId,
                descriptor.Version,
                descriptor.ArtifactDigest),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(
                         temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await source.CopyToAsync(target, cancellationToken);
        if (!await VerifyFileAsync(
                temporary, descriptor.ArtifactDigest, descriptor.ArtifactSize, cancellationToken))
        {
            File.Delete(temporary);
            throw new InvalidDataException("guest_artifact_verification_failed");
        }
        File.Move(temporary, destination, true);
        Restrict(destination);
        return destination;
    }

    public async Task<GuestSecretResponse> FetchSecretsAsync(
        GuestAssetIdentity identity,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0) return new GuestSecretResponse([]);
        using var certificate = LoadClientCertificate();
        using var handler = CreatePinnedHandler(certificate);
        using var client = new HttpClient(handler);
        var endpoint = new Uri(configuration.EnrollmentEndpoint, "secrets");
        using var response = await client.PostAsJsonAsync(
            endpoint, new GuestSecretRequest(identity, references), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GuestSecretResponse>(cancellationToken)
               ?? throw new InvalidDataException("guest_secret_response_invalid");
    }

    private ECDsa LoadOrCreateKey()
    {
        var key = ECDsa.Create();
        if (File.Exists(_keyPath))
        {
            key.ImportFromPem(File.ReadAllText(_keyPath));
            return key;
        }
        key.GenerateKey(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(_keyPath, key.ExportPkcs8PrivateKeyPem());
        Restrict(_keyPath);
        return key;
    }

    private HttpClientHandler CreatePinnedHandler(X509Certificate2? certificate)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, chain, _) =>
                chain is not null && chain.ChainElements.Cast<X509ChainElement>().Any(item =>
                    string.Equals(
                        Convert.ToHexStringLower(SHA256.HashData(item.Certificate.RawData)),
                        configuration.WorkerServerCertificateSha256,
                        StringComparison.OrdinalIgnoreCase))
        };
        if (certificate is not null) handler.ClientCertificates.Add(certificate);
        return handler;
    }

    private static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return;
        }
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                path,
                "/inheritance:r",
                "/grant:r",
                "*S-1-5-18:F",
                "*S-1-5-32-544:F"
            }
        });
        process?.WaitForExit();
        if (process is null || process.ExitCode != 0)
            throw new UnauthorizedAccessException("guest_state_acl_failed");
    }

    private static async Task<bool> VerifyFileAsync(
        string path,
        string digest,
        long size,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length != size) return false;
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        return string.Equals(actual, NormalizeDigest(digest), StringComparison.Ordinal);
    }

    private static string NormalizeDigest(string digest) =>
        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest[7..].ToLowerInvariant()
            : digest.ToLowerInvariant();
}
