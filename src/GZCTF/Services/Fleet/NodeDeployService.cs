using System.Text.RegularExpressions;
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

        try
        {
            using var ssh = new SshClient(hostAddress, username, password);
            await Task.Run(() => ssh.Connect(), token);

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

            var serverUrl = _config["Urls"]?.Split(';').First() ?? "http://localhost:8080";
            var configJson = $$"""
{
  "Agent": {
    "ServerUrl": "{{serverUrl}}",
    "NodeId": "{{node.Id}}",
    "AuthToken": "{{node.AuthToken}}",
    "ListenPort": 5001,
    "HeartbeatIntervalSeconds": 30
  }
}
""";
            ssh.RunCommand($"mkdir -p /etc/gzctf-agent && cat > /etc/gzctf-agent/appsettings.json << 'GZCTFEOF'\n{configJson}\nGZCTFEOF");

            ssh.RunCommand($"wget -q -O /usr/local/bin/gzctf-agent {serverUrl}/api/agent/download && chmod +x /usr/local/bin/gzctf-agent 2>&1 || echo AGENT_DOWNLOAD_FAILED");

            var serviceContent = """
[Unit]
Description=GZCTF Agent
After=network.target docker.service

[Service]
ExecStart=/usr/local/bin/gzctf-agent
WorkingDirectory=/etc/gzctf-agent
Restart=always

[Install]
WantedBy=multi-user.target
""";
            ssh.RunCommand($"cat > /etc/systemd/system/gzctf-agent.service << 'GZCTFEOF'\n{serviceContent}\nGZCTFEOF");
            ssh.RunCommand("systemctl daemon-reload && systemctl enable gzctf-agent && systemctl start gzctf-agent");

            ssh.Disconnect();

            node.Status = NodeStatus.Online;
            node.LastHeartbeat = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(token);

            _logger.LogInformation("Node {NodeId} deployed: caps={Caps}", node.Id, caps);

            return new NodeDeployResult
            {
                Success = true, NodeId = node.Id, NodeName = node.Name,
                Capabilities = caps, Message = $"Deployment succeeded, detected capabilities: {caps}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deploy failed for node {NodeId}, removing from database", node.Id);
            _context.WorkerNodes.Remove(node);
            await _context.SaveChangesAsync(token);

            return new NodeDeployResult
            {
                Success = false, NodeId = node.Id,
                Message = $"Connection failed: {ex.Message}"
            };
        }
    }

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
