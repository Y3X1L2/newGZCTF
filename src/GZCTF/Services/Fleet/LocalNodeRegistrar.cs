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
        var providerType = _config.GetValue<string>("ContainerProvider:Type") ?? "Docker";
        var hasDocker = providerType != "Kubernetes";
        var localNode = await context.WorkerNodes.FirstOrDefaultAsync(n => n.IsLocal, token);

        if (localNode is not null)
        {
            localNode.HostAddress = publicEntry;
            localNode.Capabilities = hasDocker ? NodeCapability.Docker : NodeCapability.None;
            localNode.Status = NodeStatus.Online;
            localNode.LastHeartbeat = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);
            _logger.LogInformation("Refreshed local server node: {Id}", localNode.Id);
            return;
        }

        var node = new WorkerNode
        {
            Name = "Local Server",
            HostAddress = publicEntry,
            AuthToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Capabilities = hasDocker ? NodeCapability.Docker : NodeCapability.None,
            IsLocal = true,
            IsSchedulable = true,
            Status = NodeStatus.Online,
            LastHeartbeat = DateTimeOffset.UtcNow,
        };

        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync(token);
        _logger.LogInformation("Registered local server node: {Id}", node.Id);
    }

    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
}
