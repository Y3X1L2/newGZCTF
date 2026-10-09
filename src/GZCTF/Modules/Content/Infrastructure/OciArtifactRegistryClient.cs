using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed record OciArtifactReference(
    string RegistryAddress,
    string Repository,
    string Tag,
    string Digest,
    long Size);

public sealed class OciArtifactRegistryFailureException(string message, HttpStatusCode statusCode)
    : InvalidOperationException(message)
{
    public bool Retryable => statusCode == HttpStatusCode.RequestTimeout ||
                             statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}

public sealed class OciArtifactRegistryClient(
    IHttpClientFactory httpClientFactory,
    ILogger<OciArtifactRegistryClient> logger)
{
    public const string ManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    private const string EmptyConfigMediaType = "application/vnd.oci.empty.v1+json";

    public async Task<bool> ExistsAsync(
        OciArtifactReference reference,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl(reference));
        request.Headers.Accept.ParseAdd(ManifestMediaType);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode)
            throw await RegistryFailureAsync("resolve OCI artifact", reference, response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("layers", out var layers) ||
            layers.ValueKind != JsonValueKind.Array || layers.GetArrayLength() != 1)
            throw new InvalidOperationException(
                $"OCI artifact {reference.Repository}:{reference.Tag} has an invalid layer manifest.");
        var layer = layers[0];
        var digest = layer.GetProperty("digest").GetString();
        var size = layer.GetProperty("size").GetInt64();
        if (!string.Equals(digest, reference.Digest, StringComparison.Ordinal) || size != reference.Size)
            throw new InvalidOperationException(
                $"OCI artifact tag {reference.Repository}:{reference.Tag} points to unexpected content.");
        return true;
    }

    public async Task<OciArtifactReference> PushFileAsync(
        string registryAddress,
        string repository,
        string tag,
        string filePath,
        string expectedSha256,
        string artifactType,
        string blobMediaType,
        IReadOnlyDictionary<string, string>? annotations,
        CancellationToken cancellationToken)
    {
        var digest = NormalizeDigest(expectedSha256);
        var path = Path.GetFullPath(filePath);
        if (!File.Exists(path)) throw new FileNotFoundException("OCI artifact source file was not found.", path);
        var reference = new OciArtifactReference(
            NormalizeRegistry(registryAddress),
            NormalizeRepository(repository),
            NormalizeTag(tag),
            $"sha256:{digest}",
            new FileInfo(path).Length);
        var actual = await ComputeSha256Async(path, cancellationToken);
        if (!string.Equals(actual, digest, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"OCI artifact digest mismatch: expected sha256:{digest}, got sha256:{actual}.");
        if (await ExistsAsync(reference, cancellationToken)) return reference;

        var client = httpClientFactory.CreateClient();
        var uploadUrl = await StartBlobUploadAsync(client, reference, cancellationToken);
        uploadUrl = await UploadBlobChunksAsync(client, uploadUrl, path, cancellationToken);
        await CompleteBlobUploadAsync(client, uploadUrl, reference.Digest, null, cancellationToken);

        var config = Encoding.UTF8.GetBytes("{}");
        var configDigest = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(config))}";
        var configUpload = await StartBlobUploadAsync(client, reference, cancellationToken);
        await CompleteBlobUploadAsync(client, configUpload, configDigest, config, cancellationToken);
        var mergedAnnotations = new Dictionary<string, string>(annotations ??
            new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            ["org.opencontainers.image.title"] = Path.GetFileName(path),
            ["org.opencontainers.image.created"] = DateTimeOffset.UtcNow.ToString("O")
        };
        var manifest = new
        {
            schemaVersion = 2,
            mediaType = ManifestMediaType,
            artifactType,
            config = new { mediaType = EmptyConfigMediaType, digest = configDigest, size = config.Length },
            layers = new[] { new { mediaType = blobMediaType, digest = reference.Digest, size = reference.Size } },
            annotations = mergedAnnotations
        };
        using var content = new StringContent(JsonSerializer.Serialize(manifest), Encoding.UTF8, ManifestMediaType);
        using var response = await client.PutAsync(ManifestUrl(reference), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await RegistryFailureAsync("push OCI artifact manifest", reference, response, cancellationToken);
        logger.LogInformation("Pushed OCI artifact {Repository}:{Tag} to {Registry}.",
            reference.Repository, reference.Tag, reference.RegistryAddress);
        return reference;
    }

    /// <summary>Verifies the manifest and every blob byte before relinquishing an old source.</summary>
    public async Task VerifyBytesAsync(OciArtifactReference reference, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromHours(2));
        var token = deadline.Token;
        if (reference.Size <= 0)
            throw new InvalidOperationException("OCI artifact source is unavailable.");
        var manifestDigest = await VerifyManifestBytesAsync(reference, token);
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"http://{reference.RegistryAddress}/v2/{reference.Repository}/blobs/{reference.Digest}");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw await RegistryFailureAsync("verify OCI artifact bytes", reference, response, token);
        if (response.Content.Headers.ContentLength is { } length && length != reference.Size)
            throw new InvalidOperationException("OCI artifact blob size differs from its source identity.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long bytes = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (read > reference.Size - bytes)
                throw new InvalidOperationException("OCI artifact blob exceeds its declared size.");
            hash.AppendData(buffer, 0, read);
            bytes += read;
        }
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (bytes != reference.Size || !string.Equals(actual, NormalizeDigest(reference.Digest), StringComparison.Ordinal))
            throw new InvalidOperationException("OCI artifact blob SHA-256 or size verification failed.");
        // A mutable tag must still name these bytes when the source change is committed.
        if (await VerifyManifestBytesAsync(reference, token) != manifestDigest)
            throw new InvalidOperationException("OCI artifact manifest changed during verification.");
    }

    async Task<string> VerifyManifestBytesAsync(OciArtifactReference reference, CancellationToken token)
    {
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl(reference));
        request.Headers.Accept.ParseAdd(ManifestMediaType);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw await RegistryFailureAsync("verify OCI artifact manifest", reference, response, token);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (body.Length + read > 1024 * 1024)
                throw new InvalidOperationException("OCI artifact manifest exceeds the verification limit.");
            body.Write(buffer, 0, read);
        }
        var bytes = body.ToArray();
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!response.Headers.TryGetValues("Docker-Content-Digest", out var values) || values.SingleOrDefault() != digest)
            throw new InvalidOperationException("OCI artifact manifest SHA-256 verification failed.");
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array ||
            layers.GetArrayLength() != 1 || layers[0].GetProperty("digest").GetString() != reference.Digest ||
            layers[0].GetProperty("size").GetInt64() != reference.Size)
            throw new InvalidOperationException("OCI artifact manifest points to unexpected content.");
        return digest;
    }

    public async Task DeleteAsync(OciArtifactReference reference, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var head = new HttpRequestMessage(HttpMethod.Head, ManifestUrl(reference));
        head.Headers.Accept.ParseAdd(ManifestMediaType);
        using var headResponse = await client.SendAsync(head, cancellationToken);
        if (headResponse.StatusCode == HttpStatusCode.NotFound) return;
        if (!headResponse.IsSuccessStatusCode)
            throw await RegistryFailureAsync("resolve OCI artifact for deletion", reference, headResponse,
                cancellationToken);
        if (!headResponse.Headers.TryGetValues("Docker-Content-Digest", out var values) ||
            string.IsNullOrWhiteSpace(values.FirstOrDefault()))
            throw new InvalidOperationException("Registry did not return Docker-Content-Digest.");
        var manifestDigest = values.First();
        using var response = await client.DeleteAsync(
            $"http://{reference.RegistryAddress}/v2/{reference.Repository}/manifests/" +
            Uri.EscapeDataString(manifestDigest), cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw await RegistryFailureAsync("delete OCI artifact", reference, response, cancellationToken);
    }

    private static async Task<Uri> StartBlobUploadAsync(
        HttpClient client,
        OciArtifactReference reference,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            $"http://{reference.RegistryAddress}/v2/{reference.Repository}/blobs/uploads/",
            null,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await RegistryFailureAsync("start OCI blob upload", reference, response, cancellationToken);
        return ResolveUploadLocation($"http://{reference.RegistryAddress}", response.Headers.Location);
    }

    private static async Task<Uri> UploadBlobChunksAsync(
        HttpClient client,
        Uri uploadUrl,
        string filePath,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 32 * 1024 * 1024;
        var buffer = new byte[chunkSize];
        long offset = 0;
        await using var stream = File.OpenRead(filePath);
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0) break;
            using var content = new ByteArrayContent(buffer, 0, read);
            content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Octet);
            content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + read - 1);
            using var response = await client.PatchAsync(uploadUrl, content, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Accepted && !response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to upload OCI blob chunk at offset {offset}: {(int)response.StatusCode} {response.StatusCode}.");
            uploadUrl = ResolveUploadLocation($"{uploadUrl.Scheme}://{uploadUrl.Authority}", response.Headers.Location);
            offset += read;
        }
        return uploadUrl;
    }

    private static async Task CompleteBlobUploadAsync(
        HttpClient client,
        Uri uploadUrl,
        string digest,
        byte[]? bytes,
        CancellationToken cancellationToken)
    {
        var separator = string.IsNullOrWhiteSpace(uploadUrl.Query) ? "?" : "&";
        var uri = new Uri($"{uploadUrl}{separator}digest={Uri.EscapeDataString(digest)}");
        using HttpContent? content = bytes is null ? null : new ByteArrayContent(bytes);
        if (content is not null)
            content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Octet);
        using var response = await client.PutAsync(uri, content, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException(
                $"Failed to complete OCI blob {digest}: {(int)response.StatusCode} {response.StatusCode}.");
    }

    private static Uri ResolveUploadLocation(string baseAddress, Uri? location) => location is null
        ? throw new InvalidOperationException("Registry did not return an upload location.")
        : location.IsAbsoluteUri ? location : new Uri($"{baseAddress}{location}");

    private static string ManifestUrl(OciArtifactReference reference) =>
        $"http://{reference.RegistryAddress}/v2/{reference.Repository}/manifests/{reference.Tag}";

    private static string NormalizeRegistry(string value) => value.Trim().TrimEnd('/');
    private static string NormalizeRepository(string value) => value.Trim().Trim('/');

    private static string NormalizeTag(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Contains('/'))
            throw new InvalidOperationException("OCI artifact tag is invalid.");
        return value;
    }

    public static string NormalizeDigest(string value)
    {
        value = value.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        if (value.Length != 64 || value.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidOperationException("OCI artifact sha256 digest is invalid.");
        return value.ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task<InvalidOperationException> RegistryFailureAsync(
        string operation,
        OciArtifactReference reference,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 1024) body = body[..1024];
        return new OciArtifactRegistryFailureException(
            $"Failed to {operation} {reference.RegistryAddress}/{reference.Repository}:{reference.Tag}: " +
            $"{(int)response.StatusCode} {response.StatusCode}. {body}", response.StatusCode);
    }
}
