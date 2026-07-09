using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services;

public class AgentMaintenanceService(IHttpClientFactory httpClientFactory, ILogger<AgentMaintenanceService> logger)
{
    private const string InstalledAgentPath = "/usr/local/bin/gzctf-agent";
    private static readonly string BackupDirectory = "/var/lib/gzctf/agent-backups";

    public async Task<AgentSyncResponse> SyncAgentAsync(AgentSyncRequest request, CancellationToken token)
    {
        if (!Uri.TryCreate(request.DownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return new AgentSyncResponse(false, "Invalid agent download URL.", CurrentVersion());

        var tempPath = Path.Combine(Path.GetTempPath(), $"gzctf-agent-sync-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var expectedSha = NormalizeSha256(request.ExpectedSha256);
            if (!string.IsNullOrWhiteSpace(expectedSha) && File.Exists(InstalledAgentPath))
            {
                var currentSha = await ComputeFileSha256Async(InstalledAgentPath, token);
                if (string.Equals(currentSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    return new AgentSyncResponse(true, "Agent is already up to date.", CurrentVersion());
            }

            await DownloadAgentAsync(uri, tempPath, token);

            var fileInfo = new FileInfo(tempPath);
            if (!fileInfo.Exists || fileInfo.Length <= 0)
                return new AgentSyncResponse(false, "Downloaded agent binary is empty.", CurrentVersion());
            if (!string.IsNullOrWhiteSpace(expectedSha))
            {
                var downloadedSha = await ComputeFileSha256Async(tempPath, token);
                if (!string.Equals(downloadedSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    return new AgentSyncResponse(false,
                        $"Downloaded agent sha256 mismatch: expected {expectedSha}, got {downloadedSha}.",
                        CurrentVersion());
            }

            var backupPath = Path.Combine(BackupDirectory, $"gzctf-agent.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            if (File.Exists(InstalledAgentPath))
                File.Copy(InstalledAgentPath, backupPath, overwrite: true);

            File.Move(tempPath, InstalledAgentPath, overwrite: true);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(InstalledAgentPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            if (request.Restart)
                _ = Task.Run(RestartAgentAfterResponseAsync, CancellationToken.None);

            return new AgentSyncResponse(true,
                request.Restart
                    ? "Agent sync requested; service restart has been scheduled."
                    : "Agent binary synchronized.",
                CurrentVersion());
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Agent self-sync failed.");
            return new AgentSyncResponse(false, $"Agent sync failed: {ex.Message}", CurrentVersion());
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task DownloadAgentAsync(Uri uri, string tempPath, CancellationToken token)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = File.Create(tempPath);
        await source.CopyToAsync(target, token);
    }

    private static async Task RestartAgentAfterResponseAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c \"systemctl restart gzctf-agent >/dev/null 2>&1 || service gzctf-agent restart >/dev/null 2>&1 || true\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is not null)
                await process.WaitForExitAsync();
        }
        catch
        {
            // The running process may be terminated by systemd during restart.
        }
    }

    private static string? CurrentVersion() =>
        typeof(AgentMaintenanceService).Assembly.GetName().Version?.ToString();

    internal static async Task<string> ComputeFileSha256Async(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            value = value["sha256:".Length..];
        return value.Length == 64 ? value.ToLowerInvariant() : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of a temporary download.
        }
    }
}
