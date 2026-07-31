using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Agent.Models;
using GZCTF.GuestControl.Contracts;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.GuestControl;

public sealed record GuestEnrollmentCertificate(
    string CertificatePem,
    string CertificateChainPem,
    string Thumbprint,
    DateTimeOffset ExpiresAt);

public sealed record GuestEnrollmentCompletion(
    GuestEnrollmentResponse Response,
    GuestBootstrapIntent Intent,
    GuestManagementLease ManagementLease);

public sealed class GuestEnrollmentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GuestManagementConfig _config;
    private readonly AgentResourceLock _resourceLock;

    public GuestEnrollmentStore(IOptions<AgentConfig> options, AgentResourceLock resourceLock)
    {
        _config = options.Value.GuestManagement;
        _resourceLock = resourceLock;
    }

    public async Task<GuestControlPrepareResponse> PrepareAsync(
        GuestControlPrepareRequest request,
        string workerServerCertificateSha256,
        CancellationToken cancellationToken)
    {
        ValidatePrepare(request);
        await using var resourceLock = await _resourceLock.AcquireAsync("guest-control:state", cancellationToken);
        Directory.CreateDirectory(_config.StateRoot);
        var existing = await ReadStateAsync(StatePath(request.Identity), cancellationToken);
        GuestManagementLease? reusableLease = null;
        if (existing is not null)
        {
            if (!IdentityEquals(existing.Identity, request.Identity) ||
                !string.Equals(existing.IntentDigest, request.Intent.IntentDigest, StringComparison.Ordinal))
                throw new InvalidOperationException("guest_control_identity_conflict");
            if (existing.EnrollmentConsumed)
                throw new InvalidOperationException("guest_enrollment_already_consumed");
            if (existing.ExpiresAt > DateTimeOffset.UtcNow)
                return BuildPrepareResponse(existing,
                    Encoding.UTF8.GetString(await DecryptAsync(
                        existing.EncryptedToken, existing.TokenNonce, existing.TokenTag,
                        IdentityBinding(existing.Identity, "token"), cancellationToken)),
                    workerServerCertificateSha256);
            reusableLease = existing.ManagementLease;
        }

        var lease = reusableLease ?? await AllocateLeaseAsync(request.Identity, cancellationToken);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var intentBytes = JsonSerializer.SerializeToUtf8Bytes(request.Intent, JsonOptions);
        var encrypted = await EncryptAsync(
            intentBytes, IdentityBinding(request.Identity, "intent"), cancellationToken);
        var encryptedToken = await EncryptAsync(
            Encoding.UTF8.GetBytes(token), IdentityBinding(request.Identity, "token"), cancellationToken);
        var encryptedSecrets = await EncryptAsync(
            JsonSerializer.SerializeToUtf8Bytes(
                request.Secrets ?? new Dictionary<string, string>(), JsonOptions),
            IdentityBinding(request.Identity, "secrets"), cancellationToken);
        var maximumExpiry = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_config.EnrollmentTtlMinutes, 1, 60));
        var effectiveExpiry = new[] { request.ExpiresAt, request.Intent.ExpiresAt, maximumExpiry }.Min();
        var state = new GuestEnrollmentState(
            request.Identity,
            lease,
            Hash(token),
            encryptedToken.Ciphertext,
            encryptedToken.Nonce,
            encryptedToken.Tag,
            request.Intent.IntentDigest,
            encrypted.Ciphertext,
            encrypted.Nonce,
            encrypted.Tag,
            encryptedSecrets.Ciphertext,
            encryptedSecrets.Nonce,
            encryptedSecrets.Tag,
            false,
            null,
            0,
            null,
            null,
            request.ProjectRuntimeSignals,
            effectiveExpiry,
            DateTimeOffset.UtcNow);
        await WriteStateAsync(state, cancellationToken);
        return BuildPrepareResponse(state, token, workerServerCertificateSha256);
    }

    public async Task<GuestEnrollmentCompletion> EnrollAsync(
        GuestEnrollmentEnvelope envelope,
        Func<GuestEnrollmentRequest, GuestEnrollmentCertificate> issueCertificate,
        CancellationToken cancellationToken)
    {
        await using var resourceLock = await _resourceLock.AcquireAsync("guest-control:state", cancellationToken);
        var path = StatePath(envelope.Enrollment.Identity);
        var state = await ReadStateAsync(path, cancellationToken)
            ?? throw new UnauthorizedAccessException("guest_enrollment_not_prepared");
        if (state.EnrollmentConsumed || state.ExpiresAt <= DateTimeOffset.UtcNow ||
            !FixedEquals(state.EnrollmentTokenHash, Hash(envelope.EnrollmentToken)))
            throw new UnauthorizedAccessException("guest_enrollment_token_invalid");
        GuestControlContractValidator.ValidateEnrollment(envelope.Enrollment, state.Identity);
        if (!string.Equals(envelope.Enrollment.IntentDigest, state.IntentDigest, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("guest_enrollment_intent_mismatch");

        var certificate = issueCertificate(envelope.Enrollment);
        var updated = state with
        {
            EnrollmentConsumed = true,
            CertificateThumbprint = certificate.Thumbprint,
            EncryptedToken = [],
            TokenNonce = [],
            TokenTag = [],
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await WriteStateAsync(updated, cancellationToken);
        var intent = await DecryptIntentAsync(updated, cancellationToken);
        return new GuestEnrollmentCompletion(
            new GuestEnrollmentResponse(
                GuestControlProtocol.SchemaVersion,
                certificate.CertificatePem,
                certificate.CertificateChainPem,
                certificate.ExpiresAt),
            intent,
            updated.ManagementLease);
    }

    public async Task<GuestEventDisposition> AcceptEventAsync(
        string certificateThumbprint,
        GuestLifecycleEvent guestEvent,
        Func<CancellationToken, Task> journalBeforeAcknowledge,
        CancellationToken cancellationToken)
    {
        await using var resourceLock = await _resourceLock.AcquireAsync("guest-control:state", cancellationToken);
        var state = await ReadStateAsync(StatePath(guestEvent.Identity), cancellationToken)
            ?? throw new UnauthorizedAccessException("guest_identity_not_enrolled");
        if (!state.EnrollmentConsumed ||
            !FixedEquals(state.CertificateThumbprint, certificateThumbprint))
            throw new UnauthorizedAccessException("guest_certificate_identity_mismatch");
        var disposition = GuestControlContractValidator.ValidateEvent(
            ExpectedEventIdentity(state, guestEvent),
            state.LastSequence,
            state.LastPayloadDigest,
            guestEvent);
        if (disposition != GuestEventDisposition.Accepted) return disposition;
        GuestControlContractValidator.ValidateLifecycleTransition(state.LastStage, guestEvent.Stage);
        if (state.ProjectRuntimeSignals != false)
            await journalBeforeAcknowledge(cancellationToken);
        await WriteStateAsync(state with
        {
            LastSequence = guestEvent.Sequence,
            LastPayloadDigest = guestEvent.PayloadDigest,
            LastStage = guestEvent.Stage,
            Identity = guestEvent.Identity,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        return disposition;
    }

    public async Task<GuestControlStatus?> GetStatusAsync(
        GuestAssetIdentity identity,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(StatePath(identity), cancellationToken);
        if (state is null) return null;
        GuestControlContractValidator.ValidateIdentity(identity, state.Identity, requireBootEpoch: false);
        return new GuestControlStatus(
            state.Identity,
            state.ManagementLease,
            state.EnrollmentConsumed,
            state.CertificateThumbprint,
            state.LastSequence,
            state.LastPayloadDigest,
            state.ExpiresAt,
            state.LastStage);
    }

    public async Task<GuestBootstrapIntent> GetIntentAsync(
        string certificateThumbprint,
        GuestAssetIdentity identity,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(StatePath(identity), cancellationToken)
            ?? throw new UnauthorizedAccessException("guest_identity_not_enrolled");
        GuestControlContractValidator.ValidateIdentity(state.Identity, identity, requireBootEpoch: false);
        if (!state.EnrollmentConsumed || !FixedEquals(state.CertificateThumbprint, certificateThumbprint))
            throw new UnauthorizedAccessException("guest_certificate_identity_mismatch");
        return await DecryptIntentAsync(state, cancellationToken);
    }

    public async Task<GuestSecretResponse> GetSecretsAsync(
        string certificateThumbprint,
        GuestSecretRequest request,
        CancellationToken cancellationToken)
    {
        if (request.References.Count is <= 0 or > 64 ||
            request.References.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 128) ||
            request.References.Distinct(StringComparer.Ordinal).Count() != request.References.Count)
            throw new ArgumentException("guest_secret_request_invalid", nameof(request));
        var state = await ReadStateAsync(StatePath(request.Identity), cancellationToken)
            ?? throw new UnauthorizedAccessException("guest_identity_not_enrolled");
        GuestControlContractValidator.ValidateIdentity(state.Identity, request.Identity, requireBootEpoch: true);
        if (!state.EnrollmentConsumed || !FixedEquals(state.CertificateThumbprint, certificateThumbprint))
            throw new UnauthorizedAccessException("guest_certificate_identity_mismatch");
        var intent = await DecryptIntentAsync(state, cancellationToken);
        var declared = (intent.SecretReferences ?? [])
            .ToDictionary(item => item.Reference, StringComparer.Ordinal);
        if (request.References.Any(item => !declared.ContainsKey(item)))
            throw new UnauthorizedAccessException("guest_secret_not_authorized");
        var plaintext = await DecryptAsync(
            state.EncryptedSecrets, state.SecretsNonce, state.SecretsTag,
            IdentityBinding(state.Identity, "secrets"), cancellationToken);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext, JsonOptions)
                     ?? throw new InvalidDataException("guest_secret_state_invalid");
        var secrets = request.References.Select(reference =>
        {
            if (!values.TryGetValue(reference, out var value))
                throw new InvalidDataException("guest_secret_value_missing");
            var item = declared[reference];
            return new GuestSecretValue(item.Name, item.Reference, item.TargetPath, value);
        }).ToArray();
        return new GuestSecretResponse(secrets);
    }

    public async Task<bool> RevokeAsync(
        GuestAssetIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var resourceLock = await _resourceLock.AcquireAsync("guest-control:state", cancellationToken);
        var path = StatePath(identity);
        var state = await ReadStateAsync(path, cancellationToken);
        if (state is null) return false;
        GuestControlContractValidator.ValidateIdentity(identity, state.Identity, requireBootEpoch: false);
        File.Delete(path);
        return true;
    }

    public async Task<int> RevokeVmAsync(
        string vmName,
        int generation,
        string? nativeVmId,
        CancellationToken cancellationToken)
    {
        await using var resourceLock = await _resourceLock.AcquireAsync("guest-control:state", cancellationToken);
        if (!Directory.Exists(_config.StateRoot)) return 0;
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(_config.StateRoot, "*.guest.json", SearchOption.AllDirectories))
        {
            var state = await ReadStateAsync(path, cancellationToken);
            if (state is null || state.Identity.Generation != generation ||
                !string.Equals(state.Identity.VmName, vmName, StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(nativeVmId) &&
                !string.Equals(state.Identity.NativeVmId.ToString("D"), nativeVmId,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            File.Delete(path);
            deleted++;
        }
        return deleted;
    }

    private async Task<GuestManagementLease> AllocateLeaseAsync(
        GuestAssetIdentity identity,
        CancellationToken cancellationToken)
    {
        if (_config.PrefixLength != 16 || !IPAddress.TryParse(_config.HostAddress, out var hostAddress) ||
            hostAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new InvalidOperationException("guest_management_pool_invalid");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(_config.StateRoot, "*.guest.json", SearchOption.AllDirectories))
        {
            var state = await ReadStateAsync(file, cancellationToken);
            if (state is not null) used.Add(state.ManagementLease.GuestAddress);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{identity.RuntimeId}:{identity.Generation}:{identity.AssetKey}"));
        var network = hostAddress.GetAddressBytes();
        const int poolSize = 65_278;
        var start = 2 + (BitConverter.ToUInt16(hash, 0) % poolSize);
        for (var offset = 0; offset < poolSize; offset++)
        {
            var host = 2 + ((start - 2 + offset) % poolSize);
            var address = $"{network[0]}.{network[1]}.{host / 256}.{host % 256}";
            if (used.Contains(address)) continue;
            var mac = $"02:7f:{hash[2]:x2}:{hash[3]:x2}:{hash[4]:x2}:{hash[5]:x2}";
            return new GuestManagementLease(
                _config.BridgeName,
                _config.HostAddress,
                _config.PrefixLength,
                address,
                mac);
        }
        throw new InvalidOperationException("guest_management_pool_exhausted");
    }

    private GuestControlPrepareResponse BuildPrepareResponse(
        GuestEnrollmentState state,
        string token,
        string serverCertificateSha256) => new(
        state.Identity,
        state.ManagementLease,
        token,
        serverCertificateSha256,
        new Uri($"https://{_config.HostAddress}:{_config.ListenPort}/api/guest/v1/enroll"),
        state.ExpiresAt);

    private async Task<GuestBootstrapIntent> DecryptIntentAsync(
        GuestEnrollmentState state,
        CancellationToken cancellationToken)
    {
        var plaintext = await DecryptAsync(
            state.EncryptedIntent, state.IntentNonce, state.IntentTag,
            IdentityBinding(state.Identity, "intent"), cancellationToken);
        return JsonSerializer.Deserialize<GuestBootstrapIntent>(plaintext, JsonOptions)
               ?? throw new InvalidDataException("guest_intent_invalid");
    }

    private async Task<byte[]> DecryptAsync(
        byte[] ciphertext,
        byte[] nonce,
        byte[] tag,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        var key = await LoadOrCreateEncryptionKeyAsync(cancellationToken);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    private async Task<EncryptedPayload> EncryptAsync(
        byte[] plaintext,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        var key = await LoadOrCreateEncryptionKeyAsync(cancellationToken);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return new EncryptedPayload(ciphertext, nonce, tag);
    }

    private async Task<byte[]> LoadOrCreateEncryptionKeyAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_config.StateRoot, "intent.key");
        if (File.Exists(path)) return await File.ReadAllBytesAsync(path, cancellationToken);
        var key = RandomNumberGenerator.GetBytes(32);
        await AtomicWriteAsync(path, key, cancellationToken);
        RestrictFile(path);
        return key;
    }

    private async Task<GuestEnrollmentState?> ReadStateAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GuestEnrollmentState>(
            stream, JsonOptions, cancellationToken);
    }

    private Task WriteStateAsync(GuestEnrollmentState state, CancellationToken cancellationToken) =>
        AtomicWriteAsync(StatePath(state.Identity), JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions),
            cancellationToken);

    private string StatePath(GuestAssetIdentity identity)
    {
        if (identity.RuntimeId <= 0 || identity.Generation <= 0 || string.IsNullOrWhiteSpace(identity.AssetKey))
            throw new ArgumentException("guest_identity_invalid", nameof(identity));
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.AssetKey)))[..24];
        return Path.Combine(_config.StateRoot, $"runtime-{identity.RuntimeId}",
            $"generation-{identity.Generation}", $"{key}.guest.json");
    }

    private static async Task AtomicWriteAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            await using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, path, true);
            RestrictFile(path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ValidatePrepare(GuestControlPrepareRequest request)
    {
        GuestControlContractValidator.ValidateIdentity(request.Identity, request.Intent.Identity,
            requireBootEpoch: false);
        if (request.Intent.SchemaVersion != GuestControlProtocol.SchemaVersion ||
            request.Intent.GuestProtocolVersion != GuestControlProtocol.SchemaVersion ||
            request.ExpiresAt <= DateTimeOffset.UtcNow ||
            string.IsNullOrWhiteSpace(request.Intent.IntentDigest) || request.Intent.IntentDigest.Length > 128 ||
            string.IsNullOrWhiteSpace(request.Intent.PreparedArtifactDigest) ||
            request.Intent.PreparedArtifactDigest.Length > 128 ||
            !string.Equals(request.Intent.IntentDigest, request.Intent.IntentDigest.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("guest_prepare_invalid", nameof(request));
        var secrets = request.Secrets ?? new Dictionary<string, string>();
        if (secrets.Count > 64 || secrets.Keys.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 128) ||
            secrets.Values.Any(item => item is null || item.Length > 32_768))
            throw new ArgumentException("guest_secret_state_invalid", nameof(request));
        var declared = (request.Intent.SecretReferences ?? [])
            .Select(item => item.Reference).ToHashSet(StringComparer.Ordinal);
        if (secrets.Keys.Any(item => !declared.Contains(item)))
            throw new ArgumentException("guest_secret_not_declared", nameof(request));
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null) return false;
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool IdentityEquals(GuestAssetIdentity left, GuestAssetIdentity right) => left == right;

    private static byte[] IdentityBinding(GuestAssetIdentity identity, string purpose) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{purpose}:{Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity with { BootEpoch = 0 }, JsonOptions)))}"));

    private static GuestAssetIdentity ExpectedEventIdentity(
        GuestEnrollmentState state,
        GuestLifecycleEvent guestEvent)
    {
        GuestControlContractValidator.ValidateBootTransition(state.Identity, state.LastStage, guestEvent);
        return guestEvent.Identity.BootEpoch == state.Identity.BootEpoch
            ? state.Identity
            : state.Identity with { BootEpoch = guestEvent.Identity.BootEpoch };
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private sealed record EncryptedPayload(byte[] Ciphertext, byte[] Nonce, byte[] Tag);

    private sealed record GuestEnrollmentState(
        GuestAssetIdentity Identity,
        GuestManagementLease ManagementLease,
        string EnrollmentTokenHash,
        byte[] EncryptedToken,
        byte[] TokenNonce,
        byte[] TokenTag,
        string IntentDigest,
        byte[] EncryptedIntent,
        byte[] IntentNonce,
        byte[] IntentTag,
        byte[] EncryptedSecrets,
        byte[] SecretsNonce,
        byte[] SecretsTag,
        bool EnrollmentConsumed,
        string? CertificateThumbprint,
        long LastSequence,
        string? LastPayloadDigest,
        GuestLifecycleStage? LastStage,
        bool? ProjectRuntimeSignals,
        DateTimeOffset ExpiresAt,
        DateTimeOffset UpdatedAt);
}
