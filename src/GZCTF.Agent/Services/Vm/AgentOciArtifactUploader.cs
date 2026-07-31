using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.Vm;

public sealed record AgentOciUploadResult(string LayerDigest, long Size, string ManifestDigest);

public sealed class AgentOciArtifactUploader(IHttpClientFactory clients)
{
    private const string ManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    private const string ConfigMediaType = "application/vnd.gzctf.vm-prepared.config.v1+json";
    private const string LayerMediaType = "application/vnd.gzctf.vm-template.qcow2";
    private const string ArtifactType = "application/vnd.gzctf.vm-prepared.qcow2";

    public async Task<AgentOciUploadResult> UploadAsync(
        string path,
        AgentOciRegistryTarget target,
        IReadOnlyDictionary<string, string> annotations,
        CancellationToken cancellationToken)
        => await UploadCoreAsync(
            path,
            target,
            annotations,
            ConfigMediaType,
            LayerMediaType,
            ArtifactType,
            cancellationToken);

    public async Task<AgentOciUploadResult> UploadVmTemplateAsync(
        string path,
        AgentOciRegistryTarget target,
        IReadOnlyDictionary<string, string> annotations,
        CancellationToken cancellationToken)
        => await UploadCoreAsync(
            path,
            target,
            annotations,
            "application/vnd.gzctf.vm-template.config.v1+json",
            "application/octet-stream",
            "application/vnd.gzctf.vm-template.qcow2",
            cancellationToken);

    private async Task<AgentOciUploadResult> UploadCoreAsync(
        string path,
        AgentOciRegistryTarget target,
        IReadOnlyDictionary<string, string> annotations,
        string configMediaType,
        string layerMediaType,
        string artifactType,
        CancellationToken cancellationToken)
    {
        var registry = NormalizeRegistry(target.RegistryAddress);
        var repository = NormalizeRepository(target.Repository);
        var tag = NormalizeTag(target.Tag);
        var layerDigest = await DigestFileAsync(path, cancellationToken);
        var size = new FileInfo(path).Length;
        var config = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            source = annotations
        });
        var configDigest = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(config))}";
        var client = clients.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        await EnsureBlobAsync(client, registry, repository, configDigest,
            () => new MemoryStream(config, writable: false), cancellationToken);
        await EnsureBlobAsync(client, registry, repository, layerDigest,
            () => File.OpenRead(path), cancellationToken);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            mediaType = ManifestMediaType,
            artifactType,
            config = new { mediaType = configMediaType, digest = configDigest, size = config.LongLength },
            layers = new[] { new { mediaType = layerMediaType, digest = layerDigest, size } },
            annotations
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"http://{registry}/v2/{repository}/manifests/{Uri.EscapeDataString(tag)}")
        {
            Content = new ByteArrayContent(manifest)
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ManifestMediaType);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var manifestDigest = response.Headers.TryGetValues("Docker-Content-Digest", out var values)
            ? values.First()
            : $"sha256:{Convert.ToHexStringLower(SHA256.HashData(manifest))}";
        return new AgentOciUploadResult(layerDigest, size, manifestDigest);
    }

    public async Task<AgentOciUploadResult?> TryResolveAsync(
        AgentOciRegistryTarget target,
        IReadOnlyDictionary<string, string> requiredAnnotations,
        CancellationToken cancellationToken)
    {
        var registry = NormalizeRegistry(target.RegistryAddress);
        var repository = NormalizeRepository(target.Repository);
        var tag = NormalizeTag(target.Tag);
        var client = clients.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://{registry}/v2/{repository}/manifests/{Uri.EscapeDataString(tag)}");
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(ManifestMediaType));
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var manifestBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        if (!root.TryGetProperty("artifactType", out var artifactType) ||
            !string.Equals(artifactType.GetString(), ArtifactType, StringComparison.Ordinal) ||
            !root.TryGetProperty("annotations", out var annotations) ||
            annotations.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("layers", out var layers) ||
            layers.ValueKind != JsonValueKind.Array || layers.GetArrayLength() != 1)
            throw RegistryConflict("The existing OCI artifact manifest is invalid.");
        foreach (var (key, value) in requiredAnnotations)
            if (!annotations.TryGetProperty(key, out var actual) ||
                !string.Equals(actual.GetString(), value, StringComparison.Ordinal))
                throw RegistryConflict("The registry target is already owned by another operation identity.");

        var layer = layers[0];
        var layerDigest = layer.TryGetProperty("digest", out var digestElement)
            ? digestElement.GetString()
            : null;
        var size = layer.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
            ? parsedSize
            : 0;
        if (layerDigest is null || !layerDigest.StartsWith("sha256:", StringComparison.Ordinal) ||
            layerDigest.Length != 71 || !layerDigest[7..].All(Uri.IsHexDigit) || size <= 0)
            throw RegistryConflict("The existing OCI artifact layer is invalid.");
        using var blobRequest = new HttpRequestMessage(
            HttpMethod.Head, $"http://{registry}/v2/{repository}/blobs/{layerDigest}");
        using var blob = await client.SendAsync(blobRequest, cancellationToken);
        blob.EnsureSuccessStatusCode();
        if (blob.Content.Headers.ContentLength is { } contentLength && contentLength != size)
            throw RegistryConflict("The existing OCI artifact size is invalid.");

        var manifestDigest = response.Headers.TryGetValues("Docker-Content-Digest", out var values)
            ? values.First()
            : $"sha256:{Convert.ToHexStringLower(SHA256.HashData(manifestBytes))}";
        return new AgentOciUploadResult(layerDigest, size, manifestDigest);
    }

    private static async Task EnsureBlobAsync(
        HttpClient client,
        string registry,
        string repository,
        string digest,
        Func<Stream> open,
        CancellationToken cancellationToken)
    {
        using (var head = new HttpRequestMessage(
                   HttpMethod.Head, $"http://{registry}/v2/{repository}/blobs/{digest}"))
        using (var existing = await client.SendAsync(head, cancellationToken))
        {
            if (existing.StatusCode == HttpStatusCode.OK) return;
            if (existing.StatusCode != HttpStatusCode.NotFound) existing.EnsureSuccessStatusCode();
        }
        using var start = await client.PostAsync(
            $"http://{registry}/v2/{repository}/blobs/uploads/", null, cancellationToken);
        start.EnsureSuccessStatusCode();
        var location = start.Headers.Location
                       ?? throw new InvalidOperationException("oci_registry_upload_location_missing");
        var upload = location.IsAbsoluteUri ? location : new Uri(new Uri($"http://{registry}"), location);
        var separator = string.IsNullOrWhiteSpace(upload.Query) ? "?" : "&";
        var completion = new Uri(upload + separator + "digest=" + Uri.EscapeDataString(digest));
        await using var stream = open();
        using var request = new HttpRequestMessage(HttpMethod.Put, completion)
        {
            Content = new StreamContent(stream)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> DigestFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return $"sha256:{Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken))}";
    }

    private static string NormalizeRegistry(string value)
    {
        value = value.Trim().TrimEnd('/');
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = value[8..];
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("oci_registry_invalid", nameof(value));
        return value;
    }

    private static string NormalizeRepository(string value)
    {
        value = value.Trim().Trim('/').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value) || value.Split('/').Any(segment =>
                segment.Length == 0 || segment.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))))
            throw new ArgumentException("oci_repository_invalid", nameof(value));
        return value;
    }

    private static string NormalizeTag(string value)
    {
        value = value.Trim().ToLowerInvariant();
        if (value.Length is < 1 or > 128 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
            throw new ArgumentException("oci_tag_invalid", nameof(value));
        return value;
    }

    private static AgentOperationException RegistryConflict(string message) =>
        new("ImageRegistry", "oci_registry_identity_conflict", message, false,
            StatusCodes.Status409Conflict);
}
