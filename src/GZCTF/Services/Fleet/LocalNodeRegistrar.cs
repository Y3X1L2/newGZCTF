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

        if (await context.WorkerNodes.AnyAsync(n => n.IsLocal, token))
            return;

        var publicEntry = _config["ContainerProvider:PublicEntry"] ?? "localhost";
        var providerType = _config.GetValue<string>("ContainerProvider:Type") ?? "Docker";
        var hasDocker = providerType != "Kubernetes";

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
