using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/images")]
public class ImageController : ControllerBase
{
    private readonly DockerService _docker;
    private readonly ILogger<ImageController> _logger;

    public ImageController(DockerService docker, ILogger<ImageController> logger)
    { _docker = docker; _logger = logger; }

    [HttpPost("pull-docker")]
    public async Task<IActionResult> PullDockerImage([FromBody] PullDockerImageRequest request, CancellationToken token)
    {
        try
        {
            await _docker.PullImageAsync(request.Image, request.RegistryAuth, token);
            return Ok(new { message = "Image pulled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pull Docker image: {Image}", request.Image);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("download-vm")]
    public async Task<IActionResult> DownloadVmImage([FromBody] DownloadVmImageRequest request, CancellationToken token)
    {
        try
        {
            var storagePath = "/var/lib/gzctf/images";
            var fileName = request.Hash + ".qcow2";
            var destPath = Path.Combine(storagePath, fileName);

            if (System.IO.File.Exists(destPath))
                return Ok(new { message = "Image already exists" });

            if (string.IsNullOrEmpty(request.DownloadUrl))
                return BadRequest(new { message = "Download URL required" });

            var client = HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
            var response = await client.GetAsync(request.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            Directory.CreateDirectory(storagePath);

            await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            var buffer = new byte[81920];
            long bytesRead = 0;
            int lastReportPercent = -1;

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), token);
                bytesRead += read;

                if (totalBytes > 0)
                {
                    var percent = (int)(bytesRead * 100 / totalBytes);
                    if (percent != lastReportPercent && percent % 10 == 0)
                    {
                        _logger.LogInformation("VM image download progress: {Hash} {Percent}% ({MB}/{TotalMB}MB)",
                            request.Hash, percent, bytesRead / 1024 / 1024, totalBytes / 1024 / 1024);
                        lastReportPercent = percent;
                    }
                }
            }

            _logger.LogInformation("VM image downloaded: {Hash} ({MB}MB)", request.Hash, bytesRead / 1024 / 1024);
            return Ok(new { message = "Image downloaded successfully", size = bytesRead });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download VM image: {Hash}", request.Hash);
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
