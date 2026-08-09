using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using GZCTF.Agent.Models;
using GZCTF.GuestControl.Contracts;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.GuestControl;

public sealed class GuestCertificateAuthority
{
    private readonly GuestManagementConfig _config;
    private readonly object _gate = new();
    private X509Certificate2? _authority;
    private X509Certificate2? _serverCertificate;

    public GuestCertificateAuthority(IOptions<AgentConfig> options) =>
        _config = options.Value.GuestManagement;

    public X509Certificate2 GetServerCertificate()
    {
        lock (_gate)
            return _serverCertificate ??= LoadOrCreateServerCertificate();
    }

    public string GetAuthoritySha256()
    {
        var authority = GetAuthority();
        return Convert.ToHexStringLower(SHA256.HashData(authority.RawData));
    }

    public string GetServerCertificateSha256() =>
        Convert.ToHexStringLower(SHA256.HashData(GetServerCertificate().RawData));

    public X509Certificate2 GetAuthorityCertificate() =>
        X509CertificateLoader.LoadCertificate(GetAuthority().RawData);

    public GuestEnrollmentCertificate IssueClientCertificate(GuestEnrollmentRequest enrollment)
    {
        var signingRequest = CertificateRequest.LoadSigningRequestPem(
            enrollment.CertificateSigningRequestPem,
            HashAlgorithmName.SHA256,
            CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
        if (!string.Equals(signingRequest.PublicKey.Oid?.Value, "1.2.840.10045.2.1", StringComparison.Ordinal) ||
            signingRequest.PublicKey.EncodedParameters?.RawData is not { } parameters ||
            !parameters.AsSpan().SequenceEqual(
                Convert.FromHexString("06082A8648CE3D030107")))
            throw new ArgumentException("guest_csr_must_use_ecdsa_p256", nameof(enrollment));

        var identityDigest = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(enrollment.Identity))));
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN=gzctf-{identityDigest}"),
            signingRequest.PublicKey,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(signingRequest.PublicKey, false));

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(Math.Clamp(_config.ClientCertificateLifetimeMinutes, 5, 1440));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        var authority = GetAuthority();
        using var authorityKey = authority.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("guest_ca_private_key_missing");
        var signatureGenerator = X509SignatureGenerator.CreateForRSA(
            authorityKey, RSASignaturePadding.Pkcs1);
        using var certificate = request.Create(
            authority.SubjectName,
            signatureGenerator,
            now.AddMinutes(-1),
            expiresAt,
            serial);
        return new GuestEnrollmentCertificate(
            certificate.ExportCertificatePem(),
            GetAuthority().ExportCertificatePem(),
            certificate.Thumbprint,
            expiresAt);
    }

    public bool IsIssuedClientCertificate(X509Certificate2 certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(GetAuthority());
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        return chain.Build(certificate);
    }

    private X509Certificate2 GetAuthority()
    {
        lock (_gate)
            return _authority ??= LoadOrCreateAuthority();
    }

    private X509Certificate2 LoadOrCreateAuthority()
    {
        Directory.CreateDirectory(_config.StateRoot);
        var certificatePath = Path.Combine(_config.StateRoot, "ca-cert.pem");
        var keyPath = Path.Combine(_config.StateRoot, "ca-key.pem");
        if (File.Exists(certificatePath) && File.Exists(keyPath))
        {
            using var persistedCertificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
            var existingAuthority = X509CertificateLoader.LoadPkcs12(
                persistedCertificate.Export(X509ContentType.Pkcs12), null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
            EnsureAuthorityMetadata(existingAuthority);
            return existingAuthority;
        }

        using var key = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=GZCTF Worker Guest CA",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(5));
        AtomicWrite(certificatePath, certificate.ExportCertificatePem());
        AtomicWrite(keyPath, key.ExportPkcs8PrivateKeyPem());
        RestrictFile(keyPath);
        var persisted = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
        var loadedAuthority = X509CertificateLoader.LoadPkcs12(
            persisted.Export(X509ContentType.Pkcs12), null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        EnsureAuthorityMetadata(loadedAuthority);
        return loadedAuthority;
    }

    private X509Certificate2 LoadOrCreateServerCertificate()
    {
        Directory.CreateDirectory(_config.StateRoot);
        var path = Path.Combine(_config.StateRoot, "server.pfx");
        if (File.Exists(path))
        {
            var existing = X509CertificateLoader.LoadPkcs12FromFile(path, null,
                ServerKeyStorageFlags());
            if (IsValidServerCertificate(existing)) return existing;
            existing.Dispose();
        }

        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=GZCTF Guest Management",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Parse(_config.HostAddress));
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        var authority = GetAuthority();
        using var authorityKey = authority.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("guest_ca_private_key_missing");
        var signatureGenerator = X509SignatureGenerator.CreateForRSA(
            authorityKey, RSASignaturePadding.Pkcs1);
        using var publicCertificate = request.Create(
            authority.SubjectName,
            signatureGenerator,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1),
            serial);
        using var certificate = publicCertificate.CopyWithPrivateKey(key);
        var pfx = certificate.Export(X509ContentType.Pkcs12);
        AtomicWrite(path, pfx);
        RestrictFile(path);
        return X509CertificateLoader.LoadPkcs12(pfx, null,
            ServerKeyStorageFlags());
    }

    private static X509KeyStorageFlags ServerKeyStorageFlags() => OperatingSystem.IsWindows()
        ? X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet
        : X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;

    private static bool IsValidServerCertificate(X509Certificate2 certificate)
    {
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        var enhancedUsage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
        return certificate.HasPrivateKey && certificate.GetRSAPublicKey() is not null &&
               certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(1) &&
               usage is not null &&
               usage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature) &&
               usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment) &&
               enhancedUsage?.EnhancedKeyUsages.Cast<Oid>().Any(item =>
                   item.Value == "1.3.6.1.5.5.7.3.1") == true;
    }

    private static void AtomicWrite(string path, string content) =>
        AtomicWrite(path, System.Text.Encoding.UTF8.GetBytes(content));

    private static void AtomicWrite(string path, byte[] content)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private void EnsureAuthorityMetadata(X509Certificate2 authority)
    {
        var path = Path.Combine(_config.StateRoot, "ca-metadata.json");
        var expected = new GuestCaMetadata(
            1,
            "rsa-3072-sha256",
            authority.Thumbprint,
            authority.NotBefore.ToUniversalTime(),
            authority.NotAfter.ToUniversalTime());
        if (File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize<GuestCaMetadata>(File.ReadAllText(path));
            if (existing is null || existing.SchemaVersion != expected.SchemaVersion ||
                !string.Equals(existing.Algorithm, expected.Algorithm, StringComparison.Ordinal) ||
                !string.Equals(existing.Thumbprint, expected.Thumbprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("guest_ca_metadata_mismatch");
            return;
        }
        AtomicWrite(path, JsonSerializer.Serialize(expected));
    }

    private sealed record GuestCaMetadata(
        int SchemaVersion,
        string Algorithm,
        string Thumbprint,
        DateTimeOffset CreatedAt,
        DateTimeOffset NotAfter);
}
