using System.Diagnostics;

namespace GZCTF.Services.Docker;

/// <summary>
/// One-click Docker Compose deployment and cleanup.
/// </summary>
public class DockerComposeDeployer
{
    private readonly ILogger<DockerComposeDeployer> _logger;

    public DockerComposeDeployer(ILogger<DockerComposeDeployer> logger) => _logger = logger;

    /// <summary>
    /// Deploy all services using docker compose.
    /// </summary>
    public async Task<string> DeployAsync(string composeFile, CancellationToken token)
    {
        _logger.LogInformation("Deploying with docker compose");
        var psi = new ProcessStartInfo
        {
            FileName = "docker", Arguments = $"compose -f {composeFile} up -d",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        using var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(token);
        var error = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Docker compose deploy failed: {error}");
        _logger.LogInformation("Deploy complete");
        return output.Trim();
    }

    /// <summary>
    /// One-click cleanup: stop and remove all containers, volumes, networks.
    /// </summary>
    public async Task<string> CleanupAsync(string composeFile, CancellationToken token)
    {
        _logger.LogInformation("Cleaning up with docker compose");
        var psi = new ProcessStartInfo
        {
            FileName = "docker", Arguments = $"compose -f {composeFile} down -v --remove-orphans",
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        using var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        _logger.LogInformation("Cleanup complete");
        return output.Trim();
    }
}
