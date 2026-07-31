using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Storage.Interface;
using Microsoft.AspNetCore.DataProtection;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed record TeamLabCaptureUploadGrant(
    Guid CaptureId,
    Guid SegmentId,
    Guid WorkerNodeId,
    long ExpectedBytes,
    long MaxBytes,
    string ExpectedSha256);

public sealed class TeamLabCaptureUploadTokenService(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector _protector = provider
        .CreateProtector("GZCTF.TeamLab.CaptureUpload.v1")
        .ToTimeLimitedDataProtector();

    public string Issue(TeamLabCaptureUploadGrant grant, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        return _protector.Protect(JsonSerializer.Serialize(grant), lifetime);
    }

    public bool TryValidate(string token, out TeamLabCaptureUploadGrant grant)
    {
        grant = null!;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var payload = _protector.Unprotect(token, out _);
            var parsed = JsonSerializer.Deserialize<TeamLabCaptureUploadGrant>(payload);
            if (parsed is null || parsed.CaptureId == Guid.Empty || parsed.SegmentId == Guid.Empty ||
                parsed.WorkerNodeId == Guid.Empty || parsed.ExpectedBytes <= 0 ||
                parsed.MaxBytes < parsed.ExpectedBytes || !IsSha256(parsed.ExpectedSha256))
                return false;
            grant = parsed with { ExpectedSha256 = parsed.ExpectedSha256.ToLowerInvariant() };
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return false;
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed record TeamLabCaptureArtifactWriteResult(
    bool Success,
    string Message,
    long Bytes,
    string? Sha256);

public sealed record TeamLabCaptureArchiveSegment(
    Guid Id,
    Guid ObservationPointId,
    TeamLabObservationPointKind ObservationPointKind,
    string? NetworkKey,
    string? InfrastructureKey,
    string? AssetKey,
    string ObjectPath,
    long Bytes,
    string Sha256,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UploadedAt);

public sealed record TeamLabCaptureArchiveDescriptor(
    Guid RuntimeId,
    int Generation,
    Guid CaptureId,
    string Scope,
    string? NetworkKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<TeamLabCaptureArchiveSegment> Segments)
{
    public string FileName => $"teamlab-capture-{CaptureId:N}.tar";
}

public sealed class TeamLabCaptureArtifactStore(IBlobStorage storage)
{
    public static string BuildObjectPath(
        Guid runtimeId,
        int generation,
        Guid captureId,
        Guid segmentId) =>
        $"teamlab/captures/{runtimeId:D}/{generation}/{captureId:D}/{segmentId:D}.pcapng";

    public async Task<TeamLabCaptureArtifactWriteResult> WriteSegmentAsync(
        string objectPath,
        Stream source,
        long expectedBytes,
        long maxBytes,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (expectedBytes <= 0 || maxBytes < expectedBytes || !IsSha256(expectedSha256))
            return new TeamLabCaptureArtifactWriteResult(false, "Capture upload metadata is invalid.", 0, null);

        await storage.EnsureInitializedAsync(cancellationToken);
        using var measured = new DigestingReadStream(source, maxBytes);
        try
        {
            await storage.WriteAsync(objectPath, measured, false, cancellationToken);
            var digest = measured.GetDigest();
            if (measured.BytesRead != expectedBytes ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(digest),
                    Convert.FromHexString(expectedSha256)))
            {
                await storage.DeleteAsync(objectPath, cancellationToken);
                return new TeamLabCaptureArtifactWriteResult(
                    false, "Capture upload size or digest validation failed.", measured.BytesRead, digest);
            }
            return new TeamLabCaptureArtifactWriteResult(true, "Capture segment persisted.", measured.BytesRead, digest);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or CryptographicException)
        {
            await storage.DeleteAsync(objectPath, CancellationToken.None);
            return new TeamLabCaptureArtifactWriteResult(false, "Capture segment persistence failed.", measured.BytesRead, null);
        }
    }

    public Task<bool> ExistsAsync(string objectPath, CancellationToken cancellationToken) =>
        storage.ExistsAsync(objectPath, cancellationToken);

    public Task DeleteAsync(string objectPath, CancellationToken cancellationToken) =>
        storage.DeleteAsync(objectPath, cancellationToken);

    public async Task WriteArchiveAsync(
        TeamLabCaptureArchiveDescriptor descriptor,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await using var writer = new TarWriter(destination, leaveOpen: true);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            runtimeId = descriptor.RuntimeId,
            generation = descriptor.Generation,
            captureId = descriptor.CaptureId,
            descriptor.Scope,
            descriptor.NetworkKey,
            descriptor.CreatedAt,
            descriptor.CompletedAt,
            descriptor.ExpiresAt,
            segments = descriptor.Segments.Select((segment, index) => new
            {
                index,
                segment.Id,
                segment.ObservationPointId,
                observationPointKind = segment.ObservationPointKind.ToString(),
                segment.NetworkKey,
                segment.InfrastructureKey,
                segment.AssetKey,
                segment.Bytes,
                segment.Sha256,
                segment.CompletedAt,
                segment.UploadedAt,
                file = $"segments/{index:D4}-{segment.Id:N}.pcapng"
            })
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using (var manifestStream = new MemoryStream(manifest, writable: false))
        {
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = manifestStream,
                ModificationTime = descriptor.CompletedAt ?? descriptor.CreatedAt
            };
            await writer.WriteEntryAsync(manifestEntry, cancellationToken);
        }

        var ordinal = 0;
        foreach (var segment in descriptor.Segments)
        {
            await using var source = await storage.OpenReadAsync(segment.ObjectPath, cancellationToken);
            await using var bounded = new KnownLengthReadStream(source, segment.Bytes);
            var entry = new PaxTarEntry(
                TarEntryType.RegularFile,
                $"segments/{ordinal:D4}-{segment.Id:N}.pcapng")
            {
                DataStream = bounded,
                ModificationTime = segment.UploadedAt ?? segment.CompletedAt ?? descriptor.CreatedAt
            };
            await writer.WriteEntryAsync(entry, cancellationToken);
            ordinal++;
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private sealed class DigestingReadStream(Stream inner, long maxBytes) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _finalized;
        public long BytesRead { get; private set; }

        public string GetDigest()
        {
            if (_finalized) throw new InvalidOperationException("The capture digest has already been finalized.");
            _finalized = true;
            return Convert.ToHexStringLower(_hash.GetHashAndReset());
        }

        private void Observe(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length == 0) return;
            BytesRead += buffer.Length;
            if (BytesRead > maxBytes) throw new InvalidDataException("Capture upload exceeds the authorized size.");
            _hash.AppendData(buffer);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Observe(buffer.AsSpan(offset, read));
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            Observe(buffer[..read]);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Observe(buffer.Span[..read]);
            return read;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            Observe(buffer.AsSpan(offset, read));
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hash.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class KnownLengthReadStream(Stream inner, long length) : Stream
    {
        private long _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, (int)Math.Min(count, length - _position));
            _position += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer[..Math.Min(buffer.Length, (int)(length - _position))]);
            _position += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(
                buffer[..Math.Min(buffer.Length, (int)(length - _position))], cancellationToken);
            _position += read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            offset == 0 && origin == SeekOrigin.Current ? _position : throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
