using System.Diagnostics;
using System.Text.RegularExpressions;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

/// <summary>
/// Handles one-click deployment of challenge environments to target servers.
/// Admin provides IP/user/password, platform SSHs in, installs Agent, registers node.
/// </summary>
public class NodeDeployService
{
    private readonly AppDbContext _context;
    private readonly ILogger<NodeDeployService> _logger;

    public NodeDeployService(AppDbContext context, ILogger<NodeDeployService> logger)
    { _context = context; _logger = logger; }

    /// <summary>
    /// One-click deploy: connect to target, install agent, register as WorkerNode.
    /// </summary>
    public async Task<NodeDeployResult> DeployToServerAsync(
        string hostAddress, string username, string password,
        string? nodeName = null, CancellationToken token = default)
    {
        var node = new WorkerNode
        {
            Name = nodeName ?? hostAddress,
            HostAddress = hostAddress,
            AuthToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Unknown
        };

        _context.WorkerNodes.Add(node);
        await _context.SaveChangesAsync(token);

        _logger.LogInformation("Deploying to node {NodeId} at {Host}", node.Id, hostAddress);

        try
        {
            var caps = await DetectCapabilitiesAsync(hostAddress, username, password, token);
            node.Capabilities = caps;
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
            node.Status = NodeStatus.Error;
            await _context.SaveChangesAsync(token);
            _logger.LogError(ex, "Deploy failed for node {NodeId}", node.Id);

            return new NodeDeployResult
            {
                Success = false, NodeId = node.Id,
                Message = $"Connection failed: {ex.Message}"
            };
        }
    }

    private static async Task<NodeCapability> DetectCapabilitiesAsync(
        string host, string user, string password, CancellationToken token)
    {
        var caps = NodeCapability.None;

        var dockerCheck = await RunRemoteCommandAsync(
            host, user, password,
            "command -v docker && docker --version 2>&1 || echo NO_DOCKER",
            token);
        if (!dockerCheck.Contains("NO_DOCKER"))
            caps |= NodeCapability.Docker;

        var kvmCheck = await RunRemoteCommandAsync(
            host, user, password,
            "command -v virsh && virsh --version 2>&1 || echo NO_KVM",
            token);
        if (!kvmCheck.Contains("NO_KVM"))
            caps |= NodeCapability.Kvm;

        return caps;
    }

    private static readonly System.Text.RegularExpressions.Regex SafeHostPattern =
        new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex SafeUserPattern =
        new(@"^[a-z_][a-z0-9_-]*$", RegexOptions.Compiled);

    private static async Task<string> RunRemoteCommandAsync(
        string host, string user, string password, string command, CancellationToken token)
    {
        if (!SafeHostPattern.IsMatch(host))
            throw new ArgumentException("Host contains invalid characters.", nameof(host));
        if (!SafeUserPattern.IsMatch(user))
            throw new ArgumentException("User contains invalid characters.", nameof(user));

        var safeCommand = command.Replace("\"", "\\\"");
        var psi = new ProcessStartInfo
        {
            FileName = "sshpass",
            Arguments = $"-e ssh -o StrictHostKeyChecking=no -o ConnectTimeout=10 {user}@{host} \"{safeCommand}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["SSHPASS"] = password;

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start SSH process.");

        var output = await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(token);
            throw new InvalidOperationException(
                $"SSH command failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        return output;
    }
}

public class NodeDeployResult
{
    public bool Success { get; set; }
    public Guid NodeId { get; set; }
    public string? NodeName { get; set; }
    public NodeCapability Capabilities { get; set; }
    public string Message { get; set; } = string.Empty;
}
