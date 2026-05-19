using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GZCTF.Services.Docker;

/// <summary>
/// Builds Docker images from Dockerfile content or CLI.
/// </summary>
public class DockerImageBuilder
{
    private readonly ILogger<DockerImageBuilder> _logger;

    public DockerImageBuilder(ILogger<DockerImageBuilder> logger) => _logger = logger;

    /// <summary>
    /// Build a Docker image from a Dockerfile stored in a temp directory.
    /// Returns the image tag on success.
    /// </summary>
    public async Task<string> BuildFromDockerfileAsync(
        string dockerfile, string tag, CancellationToken token)
    {
        var buildDir = Path.Combine(Path.GetTempPath(), $"gzctf-build-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(buildDir);
            await File.WriteAllTextAsync(Path.Combine(buildDir, "Dockerfile"), dockerfile, token);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"build -t {tag} {buildDir}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromMinutes(10));

            process.Start();
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                var err = await process.StandardError.ReadToEndAsync(cts.Token);
                _logger.LogError("Docker build failed for tag {Tag}: {Error}", tag, err);
                throw new InvalidOperationException($"Docker build failed: {err}");
            }

            _logger.LogInformation("Docker image built: {Tag}", tag);
            return tag;
        }
        finally
        {
            if (Directory.Exists(buildDir))
                Directory.Delete(buildDir, true);
        }
    }
}
