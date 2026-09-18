using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.GuestControl;
using GZCTF.Agent.Services.TeamLab;

namespace GZCTF.Agent.Services;

public class AgentMaintenanceService(
    IHttpClientFactory httpClientFactory,
    GuestManagementNetworkService guestManagementNetwork,
    TeamLabDataPlanePreparationService dataPlane,
    ILogger<AgentMaintenanceService> logger)
{
    private const string InstalledAgentPath = "/usr/local/bin/gzctf-agent";
    private const string AgentConfigPath = "/etc/gzctf-agent/appsettings.json";
    private static readonly string BackupDirectory = "/var/lib/gzctf/agent-backups";

    public async Task<AgentSyncResponse> SyncAgentAsync(AgentSyncRequest request, CancellationToken token)
    {
        if (!Uri.TryCreate(request.DownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return new AgentSyncResponse(false, "Invalid agent download URL.", CurrentVersion());

        Directory.CreateDirectory(Path.GetDirectoryName(InstalledAgentPath)!);
        var tempPath = CreateSiblingTemporaryPath(InstalledAgentPath);
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var expectedSha = NormalizeSha256(request.ExpectedSha256);
            var agentUpToDate = false;
            if (!string.IsNullOrWhiteSpace(expectedSha) && File.Exists(InstalledAgentPath))
            {
                var currentSha = await ComputeFileSha256Async(InstalledAgentPath, token);
                agentUpToDate = string.Equals(currentSha, expectedSha, StringComparison.OrdinalIgnoreCase);
            }

            if (request.VmControlPlane is { Enabled: true })
            {
                var network = await guestManagementNetwork.ApplyAsync(false, token);
                if (!network.Success)
                    throw new InvalidOperationException("Guest management network setup failed.");
            }
            var configChanged = request.VmControlPlane is not null &&
                                await SyncVmControlPlaneConfigAsync(request.VmControlPlane, token);
            configChanged |= request.TeamLabDataPlane is not null &&
                             await SyncTeamLabDataPlaneConfigAsync(request.TeamLabDataPlane, token);
            if (agentUpToDate && request.Restart && !string.IsNullOrWhiteSpace(expectedSha))
            {
                var runningSha = await ComputeRunningBinarySha256Async(token);
                var stale = !string.IsNullOrWhiteSpace(runningSha) &&
                            !string.Equals(runningSha, expectedSha, StringComparison.OrdinalIgnoreCase);
                if (stale)
                {
                    _ = Task.Run(RestartAgentAfterResponseAsync, CancellationToken.None);
                    return new AgentSyncResponse(true,
                        "Installed agent binary is current, but the running process is stale; restart scheduled.",
                        CurrentVersion());
                }
            }
            var dataPlaneReadiness = request.TeamLabDataPlane is null
                ? null
                : await dataPlane.ApplyAsync(request.TeamLabDataPlane, token);
            if (request.TeamLabDataPlane is { Enabled: true } && dataPlaneReadiness is { Ready: false })
                return new AgentSyncResponse(false,
                    $"OVS/OVN data-plane preparation did not converge ({dataPlaneReadiness.Code}).",
                    CurrentVersion());
            if (agentUpToDate)
            {
                if (configChanged && request.Restart)
                    _ = Task.Run(RestartAgentAfterResponseAsync, CancellationToken.None);
                return new AgentSyncResponse(
                    true,
                    configChanged && request.Restart
                        ? "Agent was current; VM control-plane configuration was synchronized and restart scheduled."
                        : dataPlaneReadiness is { Ready: true }
                        ? "Agent was current; OVS/OVN data-plane prerequisites were synchronized."
                        : dataPlaneReadiness is not null
                        ? $"Agent was current; local OVS/OVN prerequisites were prepared ({dataPlaneReadiness.Code})."
                        : configChanged
                        ? "Agent was current; managed runtime configuration was synchronized."
                        : "Agent and managed runtime configuration are already up to date.",
                    CurrentVersion());
            }

            await DownloadAsync(uri, tempPath, token);

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
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Agent self-sync failed.");
            return new AgentSyncResponse(false, $"Agent sync failed: {ex.Message}", CurrentVersion());
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task DownloadAsync(Uri uri, string tempPath, CancellationToken token)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = File.Create(tempPath);
        await source.CopyToAsync(target, token);
    }

    private static async Task<bool> SyncVmControlPlaneConfigAsync(
        AgentVmControlPlaneSyncConfig desired,
        CancellationToken token)
    {
        if (!File.Exists(AgentConfigPath))
            throw new InvalidOperationException("Agent configuration file is missing.");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(AgentConfigPath, token)) as JsonObject
                   ?? throw new InvalidDataException("Agent configuration is invalid.");
        var agent = root["Agent"] as JsonObject;
        if (agent is null)
        {
            agent = new JsonObject();
            root["Agent"] = agent;
        }

        var guestManagement = JsonSerializer.SerializeToNode(new
        {
            desired.Enabled,
            desired.BridgeName,
            desired.HostAddress,
            desired.PrefixLength,
            desired.ListenPort,
            StateRoot = desired.GuestStateRoot,
            EnrollmentTtlMinutes = 15,
            ClientCertificateLifetimeMinutes = 120
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var legacyFactoryRemoved = agent.Remove("VmImageFactory");
        var legacyBuilderRemoved = agent.Remove("VmImageBuilder");
        if (!legacyFactoryRemoved &&
            !legacyBuilderRemoved &&
            JsonNode.DeepEquals(agent["GuestManagement"], guestManagement))
            return false;

        agent["GuestManagement"] = guestManagement;
        var temporary = CreateSiblingTemporaryPath(AgentConfigPath);
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
                token);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(AgentConfigPath));
            File.Move(temporary, AgentConfigPath, true);
            return true;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task<bool> SyncTeamLabDataPlaneConfigAsync(
        TeamLabDataPlaneSyncConfig desired,
        CancellationToken token)
    {
        if (!File.Exists(AgentConfigPath))
            throw new InvalidOperationException("Agent configuration file is missing.");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(AgentConfigPath, token)) as JsonObject
                   ?? throw new InvalidDataException("Agent configuration is invalid.");
        var teamLab = root["TeamLab"] as JsonObject;
        if (teamLab is null)
        {
            teamLab = new JsonObject();
            root["TeamLab"] = teamLab;
        }
        var changed = ApplyTeamLabDataPlaneConfig(teamLab, desired);
        if (!changed) return false;

        var temporary = CreateSiblingTemporaryPath(AgentConfigPath);
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
                token);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(AgentConfigPath));
            File.Move(temporary, AgentConfigPath, true);
            return true;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    internal static bool ApplyTeamLabDataPlaneConfig(JsonObject teamLab, TeamLabDataPlaneSyncConfig desired) =>
        Set(teamLab, "Enable", desired.Enabled) |
        Set(teamLab, "OvnNorthboundEndpoint", desired.NorthboundEndpoint) |
        Set(teamLab, "OvnSouthboundEndpoint", desired.SouthboundEndpoint) |
        Set(teamLab, "OvsIntegrationBridgeName", desired.IntegrationBridgeName) |
        Set(teamLab, "ManagedDhcpLeaseSeconds", Math.Clamp(desired.ManagedDhcpLeaseSeconds, 60, 86_400));

    private static bool Set(JsonObject target, string name, string? value)
    {
        var next = value is null ? null : JsonValue.Create(value);
        if (JsonNode.DeepEquals(target[name], next)) return false;
        target[name] = next;
        return true;
    }

    private static bool Set(JsonObject target, string name, bool value)
    {
        var next = JsonValue.Create(value);
        if (JsonNode.DeepEquals(target[name], next)) return false;
        target[name] = next;
        return true;
    }

    private static bool Set(JsonObject target, string name, int value)
    {
        var next = JsonValue.Create(value);
        if (JsonNode.DeepEquals(target[name], next)) return false;
        target[name] = next;
        return true;
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

    internal static async Task<string?> ComputeRunningBinarySha256Async(CancellationToken token)
    {
        var path = OperatingSystem.IsLinux() && File.Exists("/proc/self/exe")
            ? "/proc/self/exe"
            : Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? await ComputeFileSha256Async(path, token)
            : null;
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

    internal static string CreateSiblingTemporaryPath(string installedPath)
    {
        var directory = Path.GetDirectoryName(installedPath)
                        ?? throw new InvalidOperationException("Installed artifact path has no parent directory.");
        return Path.Combine(directory, $".{Path.GetFileName(installedPath)}.sync-{Guid.NewGuid():N}");
    }
}
