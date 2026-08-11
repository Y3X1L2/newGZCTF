using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Vm;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/images")]
public class ImageController : ControllerBase
{
    private readonly DockerService _docker;
    private readonly AgentOperationGate _gate;
    private readonly AgentResourceLock _resourceLock;
    private readonly ImageTransferSingleFlight _singleFlight;
    private readonly AgentOciArtifactUploader _ociUploader;
    private readonly VmImageBackingChainInspector _backingChain;
    private readonly AgentTeamLabConfig _teamLab;
    private readonly ILogger<ImageController> _logger;

    public ImageController(DockerService docker, AgentOperationGate gate, AgentResourceLock resourceLock,
        ImageTransferSingleFlight singleFlight, AgentOciArtifactUploader ociUploader,
        VmImageBackingChainInspector backingChain,
        IOptions<AgentTeamLabConfig> teamLabOptions,
        ILogger<ImageController> logger)
    {
        _docker = docker;
        _gate = gate;
        _resourceLock = resourceLock;
        _singleFlight = singleFlight;
        _ociUploader = ociUploader;
        _backingChain = backingChain;
        _teamLab = teamLabOptions.Value;
        _logger = logger;
    }

    [HttpPost("pull-docker")]
    public async Task<IActionResult> PullDockerImage([FromBody] PullDockerImageRequest request, CancellationToken token)
    {
        await _singleFlight.RunAsync<object?>("docker:" + request.Image.Trim().ToLowerInvariant(),
            async sharedToken =>
            {
                await using var cacheLock = await _resourceLock.AcquireAsync(
                    "docker-image:" + request.Image.Trim().ToLowerInvariant(), sharedToken);
                await using var permit = await _gate.EnterAsync(AgentOperationCategory.DockerImageTransfer,
                    sharedToken);
                await _docker.PullImageAsync(request.Image, request.RegistryAuth, sharedToken);
                return null;
            }, token);
        return Ok(new { message = "Image pulled successfully" });
    }

    [HttpDelete("docker")]
    public async Task<IActionResult> DeleteDockerImage([FromQuery] string image, CancellationToken token)
    {
        await using var cacheLock = await _resourceLock.AcquireAsync(
            "docker-image:" + image.Trim().ToLowerInvariant(), token);
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.Control, token);
        await _docker.DeleteImageAsync(image, token);
        return Ok(new ImageCacheCleanupResponse(
            [new ImageCacheInventoryEntry("docker", image, await _docker.ImageExistsAsync(image, token))]));
    }

    [HttpPost("ensure-docker-registry")]
    public async Task<IActionResult> EnsureDockerRegistry([FromBody] EnsureDockerRegistryRequest request,
        CancellationToken token)
    {
        var port = Math.Clamp(request.Port, 1, 65535);
        await _docker.EnsureRegistryAsync(port, token);
        return Ok(new { message = "Docker registry is ready", port });
    }

    [HttpPost("configure-docker-registry")]
    public async Task<IActionResult> ConfigureDockerRegistry([FromBody] ConfigureDockerRegistryRequest request,
        CancellationToken token)
    {
        var registries = request.Registries.Length > 0
            ? request.Registries
            : string.IsNullOrWhiteSpace(request.Registry)
                ? []
                : [request.Registry];

        await _docker.ConfigureInsecureRegistriesAsync(registries, token);
        return Ok(new { message = "Docker registry trust configured", registries });
    }

    [HttpPost("download-vm")]
    public async Task<IActionResult> DownloadVmImage([FromBody] DownloadVmImageRequest request, CancellationToken token)
    {
        var cacheIdentity = NormalizeSha256(request.Digest) ?? NormalizeSha256(request.Hash) ??
                            request.TemplateId?.ToString() ?? request.Hash;
        var key = $"vm:{cacheIdentity}";
        var result = await _singleFlight.RunAsync(key, async sharedToken =>
        {
            await using var cacheLock = await _resourceLock.AcquireAsync("vm-image:" + cacheIdentity, sharedToken);
            await using var permit = await _gate.EnterAsync(AgentOperationCategory.VmImageTransfer, sharedToken);
            return await DownloadVmImageCoreAsync(request, sharedToken);
        }, token);
        return Ok(result);
    }

    async Task<DownloadVmImageResponse> DownloadVmImageCoreAsync(DownloadVmImageRequest request,
        CancellationToken token)
    {
        const string storagePath = "/var/lib/gzctf/images";
        var fileStem = request.TemplateId.HasValue ? request.TemplateId.Value.ToString() : request.Hash;
        var destPath = Path.Combine(storagePath, fileStem + ".qcow2");
        var expectedHash = NormalizeSha256(request.Digest) ?? NormalizeSha256(request.Hash);
        var expectedDigest = string.IsNullOrWhiteSpace(expectedHash) ? null : $"sha256:{expectedHash}";
        if (System.IO.File.Exists(destPath))
        {
            var currentHash = await ComputeSha256Async(destPath, token);
            if (string.IsNullOrWhiteSpace(expectedHash) ||
                string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return new DownloadVmImageResponse(true, "Image already exists", true, true,
                    new FileInfo(destPath).Length, $"sha256:{currentHash}");

            IReadOnlyList<VmImageBackingReference> references;
            try
            {
                references = await _backingChain.FindReferencesAsync(
                    [storagePath, _teamLab.RuntimeStateRoot], [destPath], token);
            }
            catch (InvalidOperationException exception)
            {
                throw new AgentOperationException(
                    "ImageTransfer", "image.vm.cache_reference_check_failed", exception.Message, true);
            }
            if (references.Count > 0)
                throw new AgentOperationException(
                    "ImageTransfer",
                    "image.vm.cache_in_use",
                    $"VM image cache cannot be replaced while {references.Count} overlay(s) still use it.",
                    true);
            System.IO.File.Delete(destPath);
        }

        Directory.CreateDirectory(storagePath);
        var tempPath = destPath + ".part";
        await DownloadVmImagePayloadAsync(request, tempPath, token);
        var actualHash = await ComputeSha256Async(tempPath, token);
        if (!string.IsNullOrWhiteSpace(expectedHash) &&
            !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(tempPath);
            throw new AgentOperationException(
                "ImageTransfer", "image.digest_mismatch", "VM image digest verification failed.", false);
        }
        if (request.ExpectedSize is > 0 && new FileInfo(tempPath).Length != request.ExpectedSize.Value)
        {
            TryDelete(tempPath);
            throw new AgentOperationException(
                "ImageTransfer", "image.size_mismatch", "VM image size verification failed.", false);
        }
        System.IO.File.Move(tempPath, destPath, overwrite: true);
        var size = new FileInfo(destPath).Length;
        _logger.LogInformation("VM image downloaded: {Hash} ({MB}MB)", request.Hash, size / 1024 / 1024);
        return new DownloadVmImageResponse(true, "Image downloaded successfully", false, true,
            size, expectedDigest ?? $"sha256:{actualHash}");
    }

    [HttpPost("publish-vm")]
    public async Task<IActionResult> PublishVmImage(
        [FromBody] PublishVmImageRequest request,
        CancellationToken token)
    {
        var hash = NormalizeSha256(request.Hash)
                   ?? throw new ArgumentException("VM image digest is invalid.", nameof(request));
        if (request.TemplateId <= 0 || request.ExpectedSize <= 0)
            throw new ArgumentException("VM image cache identity is invalid.", nameof(request));
        var key = $"vm-publish:{request.TemplateId}:{hash}";
        var result = await _singleFlight.RunAsync(key, async sharedToken =>
        {
            await using var cacheLock = await _resourceLock.AcquireAsync("vm-image:" + hash, sharedToken);
            await using var permit = await _gate.EnterAsync(
                AgentOperationCategory.VmImageTransfer, sharedToken);
            var path = Path.Combine("/var/lib/gzctf/images", $"{request.TemplateId}.qcow2");
            if (!System.IO.File.Exists(path))
                throw new AgentOperationException(
                    "ImageTransfer", "image.vm.cache_missing",
                    "The verified VM image cache is unavailable on this worker node.", false);
            var size = new FileInfo(path).Length;
            var actualHash = await ComputeSha256Async(path, sharedToken);
            if (size != request.ExpectedSize ||
                !string.Equals(actualHash, hash, StringComparison.Ordinal))
                throw new AgentOperationException(
                    "ImageTransfer", "image.vm.cache_verification_failed",
                    "The worker VM image cache does not match the requested artifact.", false);
            var uploaded = await _ociUploader.UploadVmTemplateAsync(
                path,
                request.RegistryTarget,
                new Dictionary<string, string>
                {
                    ["org.gzctf.vm-template.id"] = request.TemplateId.ToString(),
                    ["org.gzctf.vm-template.sha256"] = hash
                },
                sharedToken);
            if (!string.Equals(uploaded.LayerDigest, $"sha256:{hash}", StringComparison.Ordinal) ||
                uploaded.Size != size)
                throw new AgentOperationException(
                    "ImageTransfer", "image.vm.registry_verification_failed",
                    "The published VM artifact does not match the verified worker cache.", false);
            return new PublishVmImageResponse(
                true, true, size, uploaded.LayerDigest, uploaded.ManifestDigest);
        }, token);
        return Ok(result);
    }

    [HttpDelete("vm/{templateId:int}")]
    public async Task<IActionResult> DeleteVmImage(
        [FromRoute] int templateId,
        [FromQuery] string? hash,
        CancellationToken token)
    {
        var storagePath = "/var/lib/gzctf/images";
        var cacheIdentity = NormalizeSha256(hash) ?? templateId.ToString();
        await using var cacheLock = await _resourceLock.AcquireAsync("vm-image:" + cacheIdentity, token);
        await using var permit = await _gate.EnterAsync(AgentOperationCategory.Control, token);
        var targets = ResolveVmImageCachePaths(storagePath, templateId, hash)
            .Where(System.IO.File.Exists)
            .ToArray();
        if (targets.Length == 0)
            return Ok(new ImageCacheCleanupResponse(
                [new ImageCacheInventoryEntry("vm", cacheIdentity, false)]));

        // A cached template is the backing file of every VM overlay created from it, and that link
        // exists only in qcow2 metadata. Deleting it while an overlay still points at it leaves that
        // VM permanently unbootable — for any game on this node, not just the caller's.
        IReadOnlyList<VmImageBackingReference> references;
        try
        {
            references = await _backingChain.FindReferencesAsync(
                [storagePath, _teamLab.RuntimeStateRoot], targets, token);
        }
        catch (InvalidOperationException exception)
        {
            throw new AgentOperationException(
                "Control", "image.vm.cache_reference_check_failed", exception.Message, true);
        }

        if (references.Count > 0)
        {
            _logger.LogWarning(
                "Refused VM image cache delete for template {TemplateId}: {Count} overlay(s) still back onto it",
                templateId, references.Count);
            throw new AgentOperationException(
                "Control",
                "image.vm.cache_in_use",
                $"VM image cache is still the backing file of {references.Count} overlay(s) on this node.",
                true);
        }

        var removed = 0;
        foreach (var path in targets)
        {
            System.IO.File.Delete(path);
            removed++;
        }

        var present = ResolveVmImageCachePaths(storagePath, templateId, hash)
            .Any(System.IO.File.Exists);
        return Ok(new ImageCacheCleanupResponse(
            [new ImageCacheInventoryEntry("vm", cacheIdentity, present)], removed));
    }

    [HttpPost("download-bootstrap-artifact")]
    public async Task<IActionResult> DownloadBootstrapArtifact(
        [FromBody] DownloadBootstrapArtifactRequest request,
        CancellationToken token)
    {
        var digest = NormalizeSha256(request.Digest)
                     ?? throw new ArgumentException("Bootstrap artifact digest is invalid.", nameof(request));
        if (request.ProfileId == Guid.Empty || request.Version <= 0 || request.ExpectedSize <= 0)
            throw new ArgumentException("Bootstrap artifact identity is invalid.", nameof(request));
        var key = $"bootstrap:{request.ProfileId:N}:{request.Version}:{digest}";
        var result = await _singleFlight.RunAsync(key, async sharedToken =>
        {
            await using var permit = await _gate.EnterAsync(AgentOperationCategory.Control, sharedToken);
            return await DownloadBootstrapArtifactCoreAsync(request, digest, sharedToken);
        }, token);
        return Ok(result);
    }

    [HttpDelete("bootstrap-artifact/{profileId:guid}/{version:int}")]
    public IActionResult DeleteBootstrapArtifact(Guid profileId, int version)
    {
        var directory = BootstrapArtifactDirectory(profileId, version);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        return Ok(new { message = "Bootstrap artifact cache cleanup completed" });
    }

    async Task<DownloadBootstrapArtifactResponse> DownloadBootstrapArtifactCoreAsync(
        DownloadBootstrapArtifactRequest request,
        string digest,
        CancellationToken token)
    {
        var directory = BootstrapArtifactDirectory(request.ProfileId, request.Version);
        var destination = Path.Combine(directory, $"{digest}.tar.gz");
        if (System.IO.File.Exists(destination))
        {
            var current = await ComputeSha256Async(destination, token);
            if (string.Equals(current, digest, StringComparison.Ordinal) &&
                new FileInfo(destination).Length == request.ExpectedSize)
                return new DownloadBootstrapArtifactResponse(
                    true, "Bootstrap artifact already exists.", true, true, destination,
                    request.ExpectedSize, $"sha256:{digest}");
            System.IO.File.Delete(destination);
        }
        Directory.CreateDirectory(directory);
        var temporary = destination + ".part";
        var registry = NormalizeRegistryAddress(request.RegistryAddress);
        var repository = request.Repository.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(registry) || string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("Bootstrap artifact registry reference is invalid.", nameof(request));
        var client = HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        client.Timeout = TimeSpan.FromHours(2);
        var existingBytes = System.IO.File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get,
            $"http://{registry}/v2/{repository}/blobs/sha256:{digest}");
        if (existingBytes > 0) httpRequest.Headers.Range = new RangeHeaderValue(existingBytes, null);
        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
        var append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (existingBytes > 0 && !append) TryDelete(temporary);
        response.EnsureSuccessStatusCode();
        await CopyResponseToFileAsync(response, temporary, digest, append, token);
        var actual = await ComputeSha256Async(temporary, token);
        var size = new FileInfo(temporary).Length;
        if (!string.Equals(actual, digest, StringComparison.Ordinal) || size != request.ExpectedSize)
        {
            TryDelete(temporary);
            throw new AgentOperationException(
                "ImageTransfer", "bootstrap_artifact_verification_failed",
                "Bootstrap artifact digest or size verification failed.", false);
        }
        System.IO.File.Move(temporary, destination, true);
        return new DownloadBootstrapArtifactResponse(
            true, "Bootstrap artifact downloaded.", false, true, destination, size, $"sha256:{actual}");
    }

    static string BootstrapArtifactDirectory(Guid profileId, int version) =>
        Path.Combine("/var/lib/gzctf/bootstrap-profiles", profileId.ToString("N"), version.ToString());

    async Task DownloadVmImagePayloadAsync(DownloadVmImageRequest request, string tempPath, CancellationToken token)
    {
        if (HasRegistryReference(request))
        {
            await DownloadFromRegistryAsync(request, tempPath, token);
            return;
        }

        await DownloadFromUrlAsync(request, tempPath, token);
    }

    async Task DownloadFromRegistryAsync(DownloadVmImageRequest request, string tempPath, CancellationToken token)
    {
        var registry = NormalizeRegistryAddress(request.RegistryAddress);
        var repository = request.Repository?.Trim().Trim('/');
        var digest = NormalizeDigest(request.Digest) ?? NormalizeDigest(request.Hash);
        if (string.IsNullOrWhiteSpace(registry) || string.IsNullOrWhiteSpace(repository) ||
            string.IsNullOrWhiteSpace(digest))
            throw new ArgumentException("Registry address, repository and digest are required.", nameof(request));

        var url = $"http://{registry}/v2/{repository}/blobs/{digest}";
        await DownloadHttpPayloadAsync(request, tempPath, url, includeAgentAuth: false, token);
    }

    async Task DownloadFromUrlAsync(DownloadVmImageRequest request, string tempPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.DownloadUrl))
            throw new ArgumentException("Download URL is required.", nameof(request));

        await DownloadHttpPayloadAsync(request, tempPath, request.DownloadUrl, includeAgentAuth: true, token);
    }

    async Task DownloadHttpPayloadAsync(DownloadVmImageRequest request, string tempPath, string url,
        bool includeAgentAuth, CancellationToken token)
    {
        var client = HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        client.Timeout = TimeSpan.FromHours(2);
        if (includeAgentAuth && !string.IsNullOrWhiteSpace(request.AuthToken))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", request.AuthToken);

        var existingBytes = System.IO.File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
            httpRequest.Headers.Range = new RangeHeaderValue(existingBytes, null);

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
        var append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (existingBytes > 0 && !append)
            TryDelete(tempPath);

        response.EnsureSuccessStatusCode();
        await CopyResponseToFileAsync(response, tempPath, request.Hash, append, token);
    }

    static bool HasRegistryReference(DownloadVmImageRequest request) =>
        !string.IsNullOrWhiteSpace(request.RegistryAddress) &&
        !string.IsNullOrWhiteSpace(request.Repository) &&
        !string.IsNullOrWhiteSpace(request.Digest ?? request.Hash);

    async Task CopyResponseToFileAsync(HttpResponseMessage response, string tempPath, string hash, bool append,
        CancellationToken token)
    {
        var existingBytes = append && System.IO.File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
        var totalBytes = response.Content.Headers.ContentRange?.Length ??
                         (response.Content.Headers.ContentLength is { } length ? existingBytes + length : -1L);
        await using var fs = new FileStream(tempPath, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 8192, true);
        var buffer = new byte[81920];
        long bytesRead = existingBytes;
        var lastReportPercent = -1;

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), token);
            bytesRead += read;

            if (totalBytes <= 0)
                continue;

            var percent = (int)(bytesRead * 100 / totalBytes);
            if (percent == lastReportPercent || percent % 10 != 0)
                continue;

            _logger.LogInformation("VM image download progress: {Hash} {Percent}% ({MB}/{TotalMB}MB)",
                hash, percent, bytesRead / 1024 / 1024, totalBytes / 1024 / 1024);
            lastReportPercent = percent;
        }

        await fs.FlushAsync(token);
    }

    static async Task<string> ComputeSha256Async(string path, CancellationToken token)
    {
        await using var stream = System.IO.File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            value = value["sha256:".Length..];
        return value.Length == 64 ? value.ToLowerInvariant() : null;
    }

    static string? NormalizeDigest(string? value)
    {
        var sha = NormalizeSha256(value);
        return string.IsNullOrWhiteSpace(sha) ? null : $"sha256:{sha}";
    }

    static string NormalizeRegistryAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().TrimEnd('/');
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["http://".Length..];
        if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["https://".Length..];
        return normalized;
    }

    static IEnumerable<string> ResolveVmImageCachePaths(string storagePath, int templateId, string? hash)
    {
        yield return Path.Combine(storagePath, $"{templateId}.qcow2");
        yield return Path.Combine(storagePath, $"{templateId}.qcow2.part");

        var normalized = NormalizeSha256(hash);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return Path.Combine(storagePath, $"{normalized}.qcow2");
        yield return Path.Combine(storagePath, $"{normalized}.qcow2.part");
    }

    static void TryDelete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
