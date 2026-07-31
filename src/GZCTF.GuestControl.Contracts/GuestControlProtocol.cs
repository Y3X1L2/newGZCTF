using System.ComponentModel.DataAnnotations;

namespace GZCTF.GuestControl.Contracts;

public static class GuestControlProtocol
{
    public const int SchemaVersion = 1;
    public const int MinimumCompatibleVersion = 1;
    public const int PreparationContractVersion = 1;
    public const string CsrAlgorithm = "ecdsa-p256-sha256";

    public static int Negotiate(int peerMinimumVersion, int peerMaximumVersion)
    {
        if (peerMinimumVersion <= 0 || peerMaximumVersion < peerMinimumVersion)
            throw new GuestControlProtocolException("guest_protocol_range_invalid");

        var selected = Math.Min(SchemaVersion, peerMaximumVersion);
        if (selected < MinimumCompatibleVersion || selected < peerMinimumVersion)
            throw new GuestControlProtocolException("guest_protocol_incompatible");
        return selected;
    }
}

public static class GuestRuntimeSignalStageCodes
{
    public const byte ManagementLinkReady = 9;
    public const byte GuestEnrolled = 10;
    public const byte NetworkApplied = 11;
    public const byte GuestReenrolledAfterBoot = 12;
    public const byte ObservationReady = 13;
}

public sealed record GuestAssetIdentity(
    Guid OperationId,
    int RuntimeId,
    int Generation,
    string AssetKey,
    string VmName,
    Guid NativeVmId,
    long BootEpoch);

public enum GuestLifecycleStage : byte
{
    ManagementLinkReady = 1,
    GuestEnrolled = 2,
    NetworkApplied = 3,
    BootstrapRunning = 4,
    RebootRequested = 5,
    GuestReenrolledAfterBoot = 6,
    BootstrapCompleted = 7,
    ServiceHealthReady = 8,
    ObservationReady = 9,
    Failed = byte.MaxValue
}

public enum GuestLifecycleOutcome : byte
{
    Started = 0,
    Ready = 1,
    Failed = 2
}

public sealed record GuestProtocolRange(int MinimumVersion, int MaximumVersion);

public sealed record GuestEnrollmentRequest(
    int SchemaVersion,
    GuestAssetIdentity Identity,
    [param: MaxLength(16_384)] string CertificateSigningRequestPem,
    [param: MaxLength(32)] string CertificateSigningAlgorithm,
    [param: MaxLength(128)] string IntentDigest,
    DateTimeOffset RequestedAt);

public sealed record GuestEnrollmentResponse(
    int SelectedProtocolVersion,
    [param: MaxLength(16_384)] string ClientCertificatePem,
    [param: MaxLength(16_384)] string CertificateChainPem,
    DateTimeOffset ExpiresAt);

public sealed record GuestBootstrapIntent(
    int SchemaVersion,
    int GuestProtocolVersion,
    GuestAssetIdentity Identity,
    [param: MaxLength(128)] string IntentDigest,
    [param: MaxLength(128)] string PreparedArtifactDigest,
    [param: MaxLength(128)] string? BootstrapArtifactDigest,
    DateTimeOffset ExpiresAt,
    GuestServicePackageDescriptor? ServicePackage = null,
    IReadOnlyList<GuestSecretReference>? SecretReferences = null,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed record GuestServicePackageDescriptor(
    Guid ProfileId,
    int Version,
    [param: MaxLength(128)] string ArtifactDigest,
    long ArtifactSize,
    Uri ArtifactEndpoint,
    string ManifestJson,
    [param: MaxLength(256)] string ManifestSignature,
    string SigningPublicKeyPem);

public sealed record GuestSecretReference(
    [param: MaxLength(64)] string Name,
    [param: MaxLength(128)] string Reference,
    [param: MaxLength(512)] string TargetPath);

public sealed record GuestLifecycleEvent(
    int SchemaVersion,
    GuestAssetIdentity Identity,
    long Sequence,
    GuestLifecycleStage Stage,
    GuestLifecycleOutcome Outcome,
    DateTimeOffset ObservedAt,
    [param: MaxLength(128)] string PayloadDigest,
    [param: MaxLength(128)] string? ErrorCode = null,
    IReadOnlyDictionary<string, string>? Facts = null);

public sealed record GuestLifecycleCheckpoint(
    GuestAssetIdentity Identity,
    long Sequence,
    GuestLifecycleStage Stage,
    [param: MaxLength(128)] string IntentDigest,
    [param: MaxLength(128)] string PayloadDigest,
    DateTimeOffset PersistedAt);

public sealed record GuestServiceHealth(
    [param: MaxLength(128)] string Name,
    bool Ready,
    [param: MaxLength(128)] string EvidenceDigest,
    DateTimeOffset ObservedAt);

public sealed record GuestControlPrepareRequest(
    GuestAssetIdentity Identity,
    GuestBootstrapIntent Intent,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, string>? Secrets = null,
    bool ProjectRuntimeSignals = true);

public sealed record GuestManagementLease(
    string BridgeName,
    string HostAddress,
    int PrefixLength,
    string GuestAddress,
    string MacAddress);

public sealed record GuestNetworkExpectation(
    string MacAddress,
    string IpAddress,
    int PrefixLength,
    bool IsManagement);

public sealed record GuestControlPrepareResponse(
    GuestAssetIdentity Identity,
    GuestManagementLease ManagementLease,
    string EnrollmentToken,
    string WorkerServerCertificateSha256,
    Uri EnrollmentEndpoint,
    DateTimeOffset ExpiresAt);

public sealed record GuestEnrollmentEnvelope(
    string EnrollmentToken,
    GuestEnrollmentRequest Enrollment);

public sealed record GuestEnrollmentSessionResponse(
    GuestEnrollmentResponse Enrollment,
    GuestBootstrapIntent Intent,
    GuestManagementLease ManagementLease);

public sealed record GuestEventEnvelope(GuestLifecycleEvent Event);

public sealed record GuestArtifactRequest(
    GuestAssetIdentity Identity,
    Guid ProfileId,
    int Version,
    [param: MaxLength(128)] string ArtifactDigest);

public sealed record GuestSecretRequest(
    GuestAssetIdentity Identity,
    IReadOnlyList<string> References);

public sealed record GuestSecretValue(
    [param: MaxLength(64)] string Name,
    [param: MaxLength(128)] string Reference,
    [param: MaxLength(512)] string TargetPath,
    string Value);

public sealed record GuestSecretResponse(IReadOnlyList<GuestSecretValue> Secrets);

public sealed record GuestControlStatus(
    GuestAssetIdentity Identity,
    GuestManagementLease ManagementLease,
    bool EnrollmentConsumed,
    string? CertificateThumbprint,
    long LastSequence,
    string? LastPayloadDigest,
    DateTimeOffset ExpiresAt,
    GuestLifecycleStage? LastStage = null);

public sealed record GuestManagementEndpointInfo(
    string BridgeName,
    string HostAddress,
    int PrefixLength,
    int ListenPort,
    bool Healthy);

public enum GuestEventDisposition : byte
{
    Accepted = 0,
    Duplicate = 1,
    Stale = 2
}

public static class GuestControlContractValidator
{
    public static void ValidateEnrollment(GuestEnrollmentRequest request, GuestAssetIdentity expected)
    {
        GuestControlProtocol.Negotiate(request.SchemaVersion, request.SchemaVersion);
        ValidateIdentity(expected, request.Identity, requireBootEpoch: false);
        if (!string.Equals(request.CertificateSigningAlgorithm, GuestControlProtocol.CsrAlgorithm,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.CertificateSigningRequestPem) ||
            !request.CertificateSigningRequestPem.Contains("BEGIN CERTIFICATE REQUEST", StringComparison.Ordinal) ||
            request.CertificateSigningRequestPem.Contains("PRIVATE KEY", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.IntentDigest))
            throw new GuestControlProtocolException("guest_enrollment_request_invalid");
    }

    public static GuestEventDisposition ValidateEvent(
        GuestAssetIdentity expected,
        long latestSequence,
        string? latestPayloadDigest,
        GuestLifecycleEvent current)
    {
        GuestControlProtocol.Negotiate(current.SchemaVersion, current.SchemaVersion);
        ValidateIdentity(expected, current.Identity, requireBootEpoch: true);
        if (current.Sequence <= 0 || string.IsNullOrWhiteSpace(current.PayloadDigest) || current.ObservedAt == default)
            throw new GuestControlProtocolException("guest_event_invalid");
        if (current.ErrorCode?.Length > 128 || current.Facts is { Count: > 28 } ||
            current.Facts?.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 64 ||
                                      item.Value.Length > 256) == true)
            throw new GuestControlProtocolException("guest_event_bounds_exceeded");
        if (current.Sequence < latestSequence) return GuestEventDisposition.Stale;
        if (current.Sequence > latestSequence) return GuestEventDisposition.Accepted;
        if (string.Equals(current.PayloadDigest, latestPayloadDigest, StringComparison.Ordinal))
            return GuestEventDisposition.Duplicate;
        throw new GuestControlProtocolException("guest_event_sequence_conflict");
    }

    public static void ValidateBootTransition(
        GuestAssetIdentity current,
        GuestLifecycleStage? previousStage,
        GuestLifecycleEvent next)
    {
        if (next.Identity.BootEpoch == current.BootEpoch) return;
        if (previousStage == GuestLifecycleStage.RebootRequested &&
            next.Stage == GuestLifecycleStage.GuestReenrolledAfterBoot &&
            next.Identity.BootEpoch == checked(current.BootEpoch + 1))
            return;
        throw new GuestControlProtocolException("guest_boot_epoch_mismatch");
    }

    public static void ValidateLifecycleTransition(
        GuestLifecycleStage? current,
        GuestLifecycleStage next)
    {
        if (next == GuestLifecycleStage.Failed) return;
        var allowed = (current, next) switch
        {
            (null, GuestLifecycleStage.ManagementLinkReady) => true,
            (GuestLifecycleStage.ManagementLinkReady, GuestLifecycleStage.GuestEnrolled) => true,
            (GuestLifecycleStage.GuestEnrolled, GuestLifecycleStage.NetworkApplied) => true,
            (GuestLifecycleStage.NetworkApplied, GuestLifecycleStage.BootstrapRunning) => true,
            (GuestLifecycleStage.BootstrapRunning, GuestLifecycleStage.RebootRequested) => true,
            (GuestLifecycleStage.RebootRequested, GuestLifecycleStage.GuestReenrolledAfterBoot) => true,
            (GuestLifecycleStage.GuestReenrolledAfterBoot, GuestLifecycleStage.BootstrapRunning) => true,
            (GuestLifecycleStage.BootstrapRunning, GuestLifecycleStage.BootstrapCompleted) => true,
            (GuestLifecycleStage.BootstrapCompleted, GuestLifecycleStage.ServiceHealthReady) => true,
            (GuestLifecycleStage.ServiceHealthReady, GuestLifecycleStage.ObservationReady) => true,
            _ => false
        };
        if (!allowed) throw new GuestControlProtocolException("guest_lifecycle_transition_invalid");
    }

    public static void ValidateIdentity(
        GuestAssetIdentity expected,
        GuestAssetIdentity actual,
        bool requireBootEpoch)
    {
        if (actual.OperationId != expected.OperationId || actual.RuntimeId != expected.RuntimeId ||
            !string.Equals(actual.AssetKey, expected.AssetKey, StringComparison.Ordinal) ||
            !string.Equals(actual.VmName, expected.VmName, StringComparison.Ordinal))
            throw new GuestControlProtocolException("guest_identity_mismatch");
        if (actual.Generation != expected.Generation)
            throw new GuestControlProtocolException("guest_generation_stale");
        if (actual.NativeVmId != expected.NativeVmId)
            throw new GuestControlProtocolException("guest_native_vm_mismatch");
        if (requireBootEpoch && actual.BootEpoch != expected.BootEpoch)
            throw new GuestControlProtocolException("guest_boot_epoch_mismatch");
    }
}

public sealed class GuestControlProtocolException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
