using System.Diagnostics;
using System.Security.Cryptography;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class LocalNodeRegistrar : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<LocalNodeRegistrar> _logger;

    public LocalNodeRegistrar(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<LocalNodeRegistrar> logger)
    { _scopeFactory = scopeFactory; _config = config; _logger = logger; }

    public async Task StartAsync(CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var publicEntry = _config["ContainerProvider:PublicEntry"] ?? "localhost";
        var capabilities = await DetectLocalCapabilitiesAsync(token);
        var localNode = await context.WorkerNodes.FirstOrDefaultAsync(n => n.IsLocal, token);

        if (localNode is not null)
        {
            localNode.HostAddress = publicEntry;
            localNode.Capabilities = capabilities;
            localNode.Status = NodeStatus.Online;
            localNode.LastHeartbeat = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);
            _logger.LogInformation("Refreshed local server node: {Id}, capabilities={Capabilities}",
                localNode.Id, capabilities);
            return;
        }

        var node = new WorkerNode
        {
            Name = "Local Server",
            HostAddress = publicEntry,
            AuthToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Capabilities = capabilities,
            IsLocal = true,
            IsSchedulable = true,
            Status = NodeStatus.Online,
            LastHeartbeat = DateTimeOffset.UtcNow,
        };

        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync(token);
        _logger.LogInformation("Registered local server node: {Id}, capabilities={Capabilities}",
            node.Id, capabilities);
    }

    public Task StopAsync(CancellationToken token) => Task.CompletedTask;

    private async Task<NodeCapability> DetectLocalCapabilitiesAsync(CancellationToken token)
    {
        var providerType = _config.GetValue<string>("ContainerProvider:Type") ?? "Docker";
        var capabilities = providerType != "Kubernetes"
            ? NodeCapability.Docker
            : NodeCapability.None;

        if (await HasLocalKvmAsync(token))
            capabilities |= NodeCapability.Kvm;

        return capabilities;
    }

    private async Task<bool> HasLocalKvmAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        var result = await RunCommandAsync("virsh", "-c qemu:///system list --all", token);
        if (result.ExitCode == 0)
            return true;

        _logger.LogWarning("Local KVM capability is unavailable: {Error}",
            string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        return false;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(
        string fileName, string arguments, CancellationToken token)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
                return (-1, string.Empty, $"Failed to start {fileName}");

            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);

            return (process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
