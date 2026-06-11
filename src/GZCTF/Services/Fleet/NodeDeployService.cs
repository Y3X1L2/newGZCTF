using System.Text.RegularExpressions;
using System.Text.Json;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;

namespace GZCTF.Services.Fleet;

public class NodeDeployService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<NodeDeployService> _logger;

    public NodeDeployService(AppDbContext context, IConfiguration config, ILogger<NodeDeployService> logger)
    { _context = context; _config = config; _logger = logger; }

    public async Task<NodeDeployResult> DeployToServerAsync(
        string hostAddress, string username, string password,
        string? nodeName = null, CancellationToken token = default)
    {
        if (!SafeHostPattern.IsMatch(hostAddress))
            throw new ArgumentException("Host contains invalid characters.", nameof(hostAddress));
        if (!SafeUserPattern.IsMatch(username))
            throw new ArgumentException("User contains invalid characters.", nameof(username));

        var node = new WorkerNode
        {
            Name = nodeName ?? hostAddress,
            HostAddress = hostAddress,
            AuthToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            Capabilities = NodeCapability.None,
            Status = NodeStatus.Unknown
        };

        _context.WorkerNodes.Add(node);
        await _context.SaveChangesAsync(token);

        _logger.LogInformation("Deploying to node {NodeId} at {Host}", node.Id, hostAddress);

        var deployStartedAt = DateTimeOffset.UtcNow;

        SshClient? ssh = null;
        var sudo = string.Empty;
        var remoteInstallStarted = false;

        try
        {
            ssh = new SshClient(hostAddress, username, password);
            await Task.Run(() => ssh.Connect(), token);
            sudo = DetectPrivilegePrefix(ssh);

            var caps = NodeCapability.None;
            var dockerCheck = ssh.RunCommand("command -v docker && docker --version 2>&1 || echo NO_DOCKER");
            if (!dockerCheck.Result.Contains("NO_DOCKER"))
                caps |= NodeCapability.Docker;

            var kvmCheck = ssh.RunCommand("command -v virsh && virsh --version 2>&1 || echo NO_KVM");
            if (!kvmCheck.Result.Contains("NO_KVM"))
                caps |= NodeCapability.Kvm;

            if (caps == NodeCapability.None)
            {
                _context.WorkerNodes.Remove(node);
                await _context.SaveChangesAsync(token);
                ssh.Disconnect();
                return new NodeDeployResult
                {
                    Success = false, NodeId = node.Id,
                    Message = "No Docker or KVM detected on target server"
                };
            }

            node.Capabilities = caps;
            await _context.SaveChangesAsync(token);

            var serverUrl = ResolveServerUrl(_config);
            var dotnetRoot = DetectDotnetRoot(ssh);
            if (string.IsNullOrWhiteSpace(dotnetRoot))
                throw new InvalidOperationException(
                    ".NET runtime not found on target server. Install .NET 10 runtime or provide a self-contained agent binary.");

            var configJson = BuildAgentConfigJson(serverUrl, node);
            WriteRemoteFile(ssh, sudo, "/etc/gzctf-agent/appsettings.json", configJson,
                "Write agent configuration");
            remoteInstallStarted = true;

            var agentUrl = $"{serverUrl.TrimEnd('/')}/api/agent/download";
            RunChecked(ssh, BuildAgentInstallScript(agentUrl, node.Id, sudo), "Install agent binary");

            WriteRemoteFile(ssh, sudo, "/etc/systemd/system/gzctf-agent.service",
                BuildAgentServiceContent(dotnetRoot), "Write agent systemd unit");

            RunDiagnostic(ssh, BuildAgentStartScript(sudo), "Start agent service");
            RunDiagnostic(ssh, BuildAgentVerifyScript(sudo, node.AuthToken, node.AgentPort),
                "Verify agent API");

            ssh.Disconnect();

            await WaitForHeartbeatAsync(node, deployStartedAt, token);

            _logger.LogInformation("Node {NodeId} deployed: caps={Caps}", node.Id, caps);

            return new NodeDeployResult
            {
                Success = true, NodeId = node.Id, NodeName = node.Name,
                Capabilities = caps, Message = $"Deployment succeeded, detected capabilities: {caps}"
            };
        }
        catch (Exception ex)
        {
            var liveNode = await GetLiveRegisteredNodeAsync(node.Id, deployStartedAt, token);
            if (liveNode is not null)
            {
                _logger.LogWarning(ex,
                    "Deploy step failed after node {NodeId} already sent heartbeat; treating registration as successful",
                    node.Id);

                return new NodeDeployResult
                {
                    Success = true, NodeId = liveNode.Id, NodeName = liveNode.Name,
                    Capabilities = liveNode.Capabilities,
                    Message = $"Deployment succeeded, detected capabilities: {liveNode.Capabilities}"
                };
            }

            if (remoteInstallStarted && ssh is { IsConnected: true })
                TryRollbackRemoteAgent(ssh, sudo);

            _logger.LogError(ex, "Deploy failed for node {NodeId}, removing from database", node.Id);
            await DeleteNodeAsync(node.Id, token);

            return new NodeDeployResult
            {
                Success = false, NodeId = node.Id,
                Message = $"Connection failed: {ex.Message}"
            };
        }
        finally
        {
            ssh?.Dispose();
        }
    }

    internal static string ResolveServerUrl(IConfiguration config)
    {
        var publicUrl = config["Agent:ServerPublicUrl"];
        if (!string.IsNullOrWhiteSpace(publicUrl))
            return publicUrl.TrimEnd('/');

        var urls = config["Urls"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        var firstUrl = urls.FirstOrDefault();
        var publicEntry = config["ContainerProvider:PublicEntry"];

        if (!string.IsNullOrWhiteSpace(publicEntry))
        {
            var scheme = "http";
            var port = "8080";

            if (!string.IsNullOrWhiteSpace(firstUrl) && Uri.TryCreate(firstUrl, UriKind.Absolute, out var boundUri))
            {
                scheme = boundUri.Scheme;
                if (!boundUri.IsDefaultPort)
                    port = boundUri.Port.ToString();
            }

            var entry = publicEntry.Trim().TrimEnd('/');
            var hasScheme = entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            return hasScheme ? entry : $"{scheme}://{entry}:{port}";
        }

        var routableUrl = urls.FirstOrDefault(IsRoutableServerUrl);
        if (!string.IsNullOrWhiteSpace(routableUrl))
            return routableUrl.TrimEnd('/');

        return "http://localhost:8080";
    }

    internal static bool IsRoutableServerUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        return !string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(host, "::", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(host, "[::]", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(host, "+", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(host, "*", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildAgentConfigJson(string serverUrl, WorkerNode node) =>
        JsonSerializer.Serialize(new
        {
            Agent = new
            {
                ServerUrl = serverUrl.TrimEnd('/'),
                NodeId = node.Id,
                node.AuthToken,
                ListenPort = node.AgentPort,
                HeartbeatIntervalSeconds = 30
            }
        }, new JsonSerializerOptions { WriteIndented = true });

    internal static string BuildAgentServiceContent(string dotnetRoot) => $$"""
[Unit]
Description=GZCTF Agent
After=network.target docker.service
Wants=docker.service

[Service]
Environment=DOTNET_ROOT={{dotnetRoot}}
Environment=DOTNET_ROOT_X64={{dotnetRoot}}
Environment=PATH={{dotnetRoot}}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
ExecStart=/usr/local/bin/gzctf-agent
WorkingDirectory=/etc/gzctf-agent
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
""";

    internal static string BuildAgentStartScript(string sudo) => $$"""
{{sudo}} systemctl daemon-reload
{{sudo}} systemctl enable gzctf-agent >/dev/null 2>&1 || true
{{sudo}} systemctl stop gzctf-agent >/dev/null 2>&1 || true
for pid in $(pgrep -f '(^|/)(gzctf-agent|GZCTF.Agent|manual-agent)( |$)' || true); do
  {{sudo}} kill "$pid" >/dev/null 2>&1 || true
done
sleep 1
restart_status=0
restart_output="$({{sudo}} systemctl restart gzctf-agent 2>&1)" || restart_status=$?
for i in $(seq 1 20); do
  if {{sudo}} systemctl is-active --quiet gzctf-agent; then
    exit 0
  fi
  sleep 1
done
systemctl --no-pager --full status gzctf-agent.service >&2 || true
{{sudo}} journalctl -u gzctf-agent.service -n 80 --no-pager >&2 || true
echo "systemctl restart exited with ${restart_status}: ${restart_output}" >&2
echo "Agent service did not become active" >&2
exit 1
""";

    internal static string BuildAgentVerifyScript(string sudo, string authToken, int agentPort) => $$"""
for i in $(seq 1 30); do
  if command -v curl >/dev/null 2>&1; then
    if curl -fsS -H {{BashQuote($"Authorization: Bearer {authToken}")}} http://127.0.0.1:{{agentPort}}/api/status >/dev/null; then
      exit 0
    fi
  elif command -v wget >/dev/null 2>&1; then
    if wget -q -O - --header={{BashQuote($"Authorization: Bearer {authToken}")}} http://127.0.0.1:{{agentPort}}/api/status >/dev/null; then
      exit 0
    fi
  fi
  sleep 1
done
systemctl --no-pager --full status gzctf-agent.service >&2 || true
{{sudo}} journalctl -u gzctf-agent.service -n 80 --no-pager >&2 || true
echo "Agent status endpoint did not become healthy" >&2
exit 1
""";

    private void TryRollbackRemoteAgent(SshClient ssh, string sudo)
    {
        try
        {
            RunChecked(ssh, $$"""
{{sudo}} systemctl disable --now gzctf-agent >/dev/null 2>&1 || true
{{sudo}} rm -f /etc/systemd/system/gzctf-agent.service /usr/local/bin/gzctf-agent || true
{{sudo}} rm -rf /etc/gzctf-agent || true
{{sudo}} systemctl daemon-reload >/dev/null 2>&1 || true
""", "Rollback agent installation");
        }
        catch (Exception rollbackEx)
        {
            _logger.LogWarning(rollbackEx, "Failed to roll back agent installation");
        }
    }

    private async Task<WorkerNode?> GetLiveRegisteredNodeAsync(Guid nodeId, DateTimeOffset deployStartedAt,
        CancellationToken token)
    {
        var liveNode = await _context.WorkerNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == nodeId, token);

        if (liveNode is null)
            return null;

        return liveNode.Status == NodeStatus.Online
               && liveNode.LastHeartbeat.HasValue
               && liveNode.LastHeartbeat.Value >= deployStartedAt
            ? liveNode
            : null;
    }

    private Task DeleteNodeAsync(Guid nodeId, CancellationToken token) =>
        _context.WorkerNodes.Where(n => n.Id == nodeId).ExecuteDeleteAsync(token);

    private async Task WaitForHeartbeatAsync(WorkerNode node, DateTimeOffset deployStartedAt,
        CancellationToken token)
    {
        for (var i = 0; i < 20; i++)
        {
            await _context.Entry(node).ReloadAsync(token);
            if (node.Status == NodeStatus.Online
                && node.LastHeartbeat.HasValue
                && node.LastHeartbeat.Value >= deployStartedAt)
                return;

            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }

        throw new InvalidOperationException("Agent service started, but no heartbeat was received by the platform.");
    }

    private static string DetectPrivilegePrefix(SshClient ssh)
    {
        var id = RunChecked(ssh, "id -u", "Detect remote user id").Result.Trim();
        if (id == "0")
            return string.Empty;

        var sudo = ssh.RunCommand("sudo -n true 2>/dev/null");
        if (sudo.ExitStatus == 0)
            return "sudo -n";

        throw new InvalidOperationException("Target user must be root or have passwordless sudo privileges to install the agent service.");
    }

    private static string DetectDotnetRoot(SshClient ssh)
    {
        var command = RunChecked(ssh, BuildDotnetRootDetectScript(), "Detect .NET runtime");

        return command.Result.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim()
               ?? string.Empty;
    }

    internal static string BuildDotnetRootDetectScript() =>
        "for p in \"$(command -v dotnet 2>/dev/null || true)\" /usr/share/dotnet/dotnet /usr/local/share/dotnet/dotnet /usr/bin/dotnet; do " +
        "[ -n \"$p\" ] && [ -x \"$p\" ] || continue; " +
        "resolved=\"$(readlink -f \"$p\" 2>/dev/null || printf \"%s\\n\" \"$p\")\"; " +
        "dirname \"$resolved\"; " +
        "break; " +
        "done";

    internal static string BuildAgentInstallScript(string agentUrl, Guid nodeId, string sudo) =>
        $$"""
tmp="/tmp/gzctf-agent-{{nodeId:N}}"
rm -f "$tmp"
download_status=127
command -v wget >/dev/null 2>&1 && wget -q -O "$tmp" {{BashQuote(agentUrl)}} && download_status=0
[ "$download_status" -eq 0 ] || { command -v curl >/dev/null 2>&1 && curl -fsSL {{BashQuote(agentUrl)}} -o "$tmp" && download_status=0; }
[ "$download_status" -eq 0 ] || { echo "wget or curl is required to download the agent" >&2; exit 127; }
test -s "$tmp"
chmod +x "$tmp"
{{sudo}} install -m 0755 "$tmp" /usr/local/bin/gzctf-agent
rm -f "$tmp"
{{sudo}} test -x /usr/local/bin/gzctf-agent
""";

    private static void WriteRemoteFile(SshClient ssh, string sudo, string remotePath, string content, string step)
    {
        var tmp = $"/tmp/gzctf-agent-{Guid.NewGuid():N}";
        RunChecked(ssh, $$"""
cat > {{BashQuote(tmp)}} <<'GZCTFEOF'
{{content}}
GZCTFEOF
{{sudo}} mkdir -p {{BashQuote(Path.GetDirectoryName(remotePath) ?? "/")}}
{{sudo}} install -m 0644 {{BashQuote(tmp)}} {{BashQuote(remotePath)}}
rm -f {{BashQuote(tmp)}}
""", step);
    }

    private static SshCommand RunChecked(SshClient ssh, string command, string step)
    {
        var script = $"set -euo pipefail\n{NormalizeShellScript(command)}";
        var result = ssh.RunCommand($"bash -lc {BashQuote(script)}");
        if (result.ExitStatus == 0)
            return result;

        var output = string.Join('\n',
            new[] { result.Error, result.Result }.Where(s => !string.IsNullOrWhiteSpace(s)))
            .Trim();
        if (output.Length > 800)
            output = output[..800];

        throw new InvalidOperationException($"{step} failed: {output}");
    }

    private void RunDiagnostic(SshClient ssh, string command, string step)
    {
        try
        {
            RunChecked(ssh, command, step);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex,
                "{Step} did not complete; continuing until platform heartbeat confirms registration",
                step);
        }
    }

    internal static string NormalizeShellScript(string command) =>
        command.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string BashQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    private static readonly Regex SafeHostPattern =
        new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex SafeUserPattern =
        new(@"^[a-z_][a-z0-9_-]*$", RegexOptions.Compiled);
}

public class NodeDeployResult
{
    public bool Success { get; set; }
    public Guid NodeId { get; set; }
    public string? NodeName { get; set; }
    public NodeCapability Capabilities { get; set; }
    public string Message { get; set; } = string.Empty;
}
