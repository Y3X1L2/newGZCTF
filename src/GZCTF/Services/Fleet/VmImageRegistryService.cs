using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public sealed record VmImageArtifactReference(
    string RegistryAddress,
    string Repository,
    string Tag,
    string Digest);

public class VmImageRegistryService(
    IOptions<DockerRegistrySettings> options,
    IHttpClientFactory httpClientFactory,
    ILogger<VmImageRegistryService> logger)
{
    const string ArtifactType = "application/vnd.gzctf.vm-template.qcow2";
    const string BlobMediaType = "application/octet-stream";
    const string ManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    readonly DockerRegistrySettings _settings = options.Value;

    public VmImageArtifactReference BuildReference(ImageTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.ImageHash))
            throw new InvalidOperationException($"VM template {template.Id} has no image hash.");

        var repository = BuildRepository(template.Id);
        return new VmImageArtifactReference(
            _settings.NormalizedAddress,
            repository,
            template.ImageHash,
            $"sha256:{template.ImageHash}");
    }

    public virtual async Task<VmImageArtifactReference> EnsureArtifactAsync(ImageTemplate template,
        CancellationToken token = default)
    {
        if (template.ImageType == ImageType.Docker)
            throw new InvalidOperationException("Docker image templates cannot be pushed as VM artifacts.");
        if (string.IsNullOrWhiteSpace(template.ImageHash))
            throw new InvalidOperationException($"VM template {template.Name} ({template.Id}) has no image hash.");

        var reference = BuildReference(template);
        if (await ManifestExistsAsync(reference, token))
            return reference;

        if (string.IsNullOrWhiteSpace(template.LocalFilePath))
            throw new InvalidOperationException(
                $"VM template {template.Name} ({template.Id}) has no local file path for registry bootstrap.");
        var path = Path.GetFullPath(template.LocalFilePath);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"VM template file for {template.Name} ({template.Id}) was not found.", path);

        await PushArtifactAsync(reference, path, template.ImageHash, token);
        return reference;
    }

    string BuildRepository(int templateId)
    {
        var ns = _settings.NormalizedNamespace;
        var path = $"gzctf/vm-template/{templateId}";
        return string.IsNullOrWhiteSpace(ns) ? path : $"{ns}/{path}";
    }

    async Task<bool> ManifestExistsAsync(VmImageArtifactReference reference, CancellationToken token)
    {
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Head,
            $"http://{reference.RegistryAddress}/v2/{reference.Repository}/manifests/{reference.Tag}");
        request.Headers.Accept.ParseAdd(ManifestMediaType);
        using var response = await client.SendAsync(request, token);
        return response.StatusCode == HttpStatusCode.OK;
    }

    async Task PushArtifactAsync(VmImageArtifactReference reference, string filePath, string expectedSha256,
        CancellationToken token)
    {
        var blobDigest = $"sha256:{NormalizeSha256(expectedSha256)}";
        var size = new FileInfo(filePath).Length;
        var client = httpClientFactory.CreateClient();
        var uploadUrl = await StartBlobUploadAsync(client, reference, token);
        uploadUrl = await UploadBlobChunksAsync(client, uploadUrl, filePath, token);
        await CompleteBlobUploadAsync(client, uploadUrl, blobDigest, token);

        var configBytes = Encoding.UTF8.GetBytes("{}");
        var configDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(configBytes)).ToLowerInvariant()}";
        var configUpload = await StartBlobUploadAsync(client, reference, token);
        await CompleteBlobUploadAsync(client, configUpload, configDigest, configBytes, token);

        var manifest = new
        {
            schemaVersion = 2,
            mediaType = ManifestMediaType,
            artifactType = ArtifactType,
            config = new
            {
                mediaType = "application/vnd.oci.empty.v1+json",
                digest = configDigest,
                size = configBytes.Length
            },
            layers = new[]
            {
                new
                {
                    mediaType = BlobMediaType,
                    digest = blobDigest,
                    size
                }
            },
            annotations = new Dictionary<string, string>
            {
                ["org.opencontainers.image.title"] = Path.GetFileName(filePath),
                ["org.gzctf.vm-template.sha256"] = NormalizeSha256(expectedSha256)
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(manifest), Encoding.UTF8, ManifestMediaType);
        using var response = await client.PutAsync(
            $"http://{reference.RegistryAddress}/v2/{reference.Repository}/manifests/{reference.Tag}",
            content, token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Failed to push VM template manifest to {reference.RegistryAddress}/{reference.Repository}:{reference.Tag}: {(int)response.StatusCode} {response.StatusCode}. {Trim(body)}");
        }

        logger.LogInformation("Pushed VM template artifact {Repository}:{Tag} to {Registry}.",
            reference.Repository, reference.Tag, reference.RegistryAddress);
    }

    static async Task<Uri> StartBlobUploadAsync(HttpClient client, VmImageArtifactReference reference,
        CancellationToken token)
    {
        using var response = await client.PostAsync(
            $"http://{reference.RegistryAddress}/v2/{reference.Repository}/blobs/uploads/",
            content: null, token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Failed to start VM artifact upload: {(int)response.StatusCode} {response.StatusCode}. {Trim(body)}");
        }

        var location = response.Headers.Location
                       ?? throw new InvalidOperationException("Registry did not return an upload location.");
        return location.IsAbsoluteUri
            ? location
            : new Uri($"http://{reference.RegistryAddress}{location}");
    }

    static async Task<Uri> UploadBlobChunksAsync(HttpClient client, Uri uploadUrl, string filePath,
        CancellationToken token)
    {
        const int chunkSize = 32 * 1024 * 1024;
        var buffer = new byte[chunkSize];
        long offset = 0;
        await using var stream = File.OpenRead(filePath);

        while (true)
        {
            var read = await stream.ReadAsync(buffer, token);
            if (read <= 0)
                break;

            using var content = new ByteArrayContent(buffer, 0, read);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(BlobMediaType);
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(
                offset, offset + read - 1);
            using var response = await client.PatchAsync(uploadUrl, content, token);
            if (response.StatusCode != HttpStatusCode.Accepted && !response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(token);
                throw new InvalidOperationException(
                    $"Failed to upload VM artifact blob chunk at {offset}: {(int)response.StatusCode} {response.StatusCode}. {Trim(body)}");
            }

            uploadUrl = ResolveUploadLocation(referenceAddress(uploadUrl), response.Headers.Location);
            offset += read;
        }

        return uploadUrl;
    }

    static string referenceAddress(Uri uploadUrl) => $"{uploadUrl.Scheme}://{uploadUrl.Authority}";

    static async Task CompleteBlobUploadAsync(HttpClient client, Uri uploadUrl, string digest, CancellationToken token)
    {
        var uri = AppendDigest(uploadUrl, digest);
        using var response = await client.PutAsync(uri, content: null, token);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Failed to complete VM artifact blob {digest}: {(int)response.StatusCode} {response.StatusCode}. {Trim(body)}");
        }
    }

    static async Task CompleteBlobUploadAsync(HttpClient client, Uri uploadUrl, string digest, byte[] bytes,
        CancellationToken token)
    {
        var uri = AppendDigest(uploadUrl, digest);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.oci.empty.v1+json");
        using var response = await client.PutAsync(uri, content, token);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(
                $"Failed to upload VM artifact config {digest}: {(int)response.StatusCode} {response.StatusCode}. {Trim(body)}");
        }
    }

    static Uri AppendDigest(Uri uploadUrl, string digest)
    {
        var separator = string.IsNullOrWhiteSpace(uploadUrl.Query) ? "?" : "&";
        return new Uri($"{uploadUrl}{separator}digest={Uri.EscapeDataString(digest)}");
    }

    static Uri ResolveUploadLocation(string baseAddress, Uri? location)
    {
        if (location is null)
            throw new InvalidOperationException("Registry did not return an upload location.");
        return location.IsAbsoluteUri ? location : new Uri($"{baseAddress}{location}");
    }

    static string NormalizeSha256(string value)
    {
        value = value.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            value = value["sha256:".Length..];
        if (value.Length != 64)
            throw new InvalidOperationException("VM image sha256 digest is invalid.");
        return value.ToLowerInvariant();
    }

    static string Trim(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        body = body.Trim();
        return body.Length <= 1024 ? body : body[..1024] + "...";
    }
}
