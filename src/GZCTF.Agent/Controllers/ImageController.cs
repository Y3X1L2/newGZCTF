using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/images")]
public class ImageController : ControllerBase
{
    private readonly DockerService _docker;
    private readonly AgentOperationGate _gate;
    private readonly ImageTransferSingleFlight _singleFlight;
    private readonly ILogger<ImageController> _logger;

    public ImageController(DockerService docker, AgentOperationGate gate,
        ImageTransferSingleFlight singleFlight, ILogger<ImageController> logger)
    { _docker = docker; _gate = gate; _singleFlight = singleFlight; _logger = logger; }

    [HttpPost("pull-docker")]
    public async Task<IActionResult> PullDockerImage([FromBody] PullDockerImageRequest request, CancellationToken token)
    {
        try
        {
            await _singleFlight.RunAsync<object?>("docker:" + request.Image.Trim().ToLowerInvariant(),
                async sharedToken =>
                {
                    await using var permit = await _gate.EnterAsync(AgentOperationCategory.DockerImageTransfer,
                        sharedToken);
                    await _docker.PullImageAsync(request.Image, request.RegistryAuth, sharedToken);
                    return null;
                }, token);
            return Ok(new { message = "Image pulled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pull Docker image: {Image}", request.Image);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpDelete("docker")]
    public async Task<IActionResult> DeleteDockerImage([FromQuery] string image, CancellationToken token)
    {
        try
        {
            await _docker.DeleteImageAsync(image, token);
            return Ok(new { message = "Docker image cache deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete Docker image: {Image}", image);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("ensure-docker-registry")]
    public async Task<IActionResult> EnsureDockerRegistry([FromBody] EnsureDockerRegistryRequest request,
        CancellationToken token)
    {
        try
        {
            var port = Math.Clamp(request.Port, 1, 65535);
            await _docker.EnsureRegistryAsync(port, token);
            return Ok(new { message = "Docker registry is ready", port });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure Docker registry on port {Port}", request.Port);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("configure-docker-registry")]
    public async Task<IActionResult> ConfigureDockerRegistry([FromBody] ConfigureDockerRegistryRequest request,
        CancellationToken token)
    {
        try
        {
            var registries = request.Registries.Length > 0
                ? request.Registries
                : string.IsNullOrWhiteSpace(request.Registry)
                    ? []
                    : [request.Registry];

            await _docker.ConfigureInsecureRegistriesAsync(registries, token);
            return Ok(new { message = "Docker registry trust configured", registries });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure Docker registry trust");
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("download-vm")]
    public async Task<IActionResult> DownloadVmImage([FromBody] DownloadVmImageRequest request, CancellationToken token)
    {
        try
        {
            var key = $"vm:{request.TemplateId?.ToString() ?? request.Hash}:{NormalizeSha256(request.Digest) ?? NormalizeSha256(request.Hash)}";
            var result = await _singleFlight.RunAsync(key, async sharedToken =>
            {
                await using var permit = await _gate.EnterAsync(AgentOperationCategory.VmImageTransfer, sharedToken);
                return await DownloadVmImageCoreAsync(request, sharedToken);
            }, token);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download VM image: {Hash}", request.Hash);
            return StatusCode(500, new DownloadVmImageResponse(false, ex.Message, false, false, null, null));
        }
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
            throw new InvalidOperationException(
                $"VM image sha256 mismatch: expected {expectedHash}, got {actualHash}.");
        }
        if (request.ExpectedSize is > 0 && new FileInfo(tempPath).Length != request.ExpectedSize.Value)
        {
            var actualSize = new FileInfo(tempPath).Length;
            TryDelete(tempPath);
            throw new InvalidOperationException(
                $"VM image size mismatch: expected {request.ExpectedSize.Value}, got {actualSize}.");
        }
        System.IO.File.Move(tempPath, destPath, overwrite: true);
        var size = new FileInfo(destPath).Length;
        _logger.LogInformation("VM image downloaded: {Hash} ({MB}MB)", request.Hash, size / 1024 / 1024);
        return new DownloadVmImageResponse(true, "Image downloaded successfully", false, true,
            size, expectedDigest ?? $"sha256:{actualHash}");
    }

    [HttpDelete("vm/{templateId:int}")]
    public IActionResult DeleteVmImage([FromRoute] int templateId, [FromQuery] string? hash)
    {
        try
        {
            var storagePath = "/var/lib/gzctf/images";
            var removed = 0;
            foreach (var path in ResolveVmImageCachePaths(storagePath, templateId, hash))
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    removed++;
                }
            }

            return Ok(new { message = "VM image cache cleanup completed", removed });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete VM image cache for template {TemplateId}", templateId);
            return StatusCode(500, new { message = ex.Message });
        }
    }

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
            throw new InvalidOperationException("Registry address, repository and digest are required.");

        var url = $"http://{registry}/v2/{repository}/blobs/{digest}";
        await DownloadHttpPayloadAsync(request, tempPath, url, includeAgentAuth: false, token);
    }

    async Task DownloadFromUrlAsync(DownloadVmImageRequest request, string tempPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.DownloadUrl))
            throw new InvalidOperationException("Download URL required");

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
