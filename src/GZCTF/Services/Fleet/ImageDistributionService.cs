using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class ImageDistributionService
{
    private readonly AppDbContext _context;
    private readonly AgentClient _agentClient;
    private readonly ILogger<ImageDistributionService> _logger;

    public ImageDistributionService(AppDbContext context, AgentClient agentClient, ILogger<ImageDistributionService> logger)
    { _context = context; _agentClient = agentClient; _logger = logger; }

    public async Task DistributeToCapableNodesAsync(ImageTemplate template, CancellationToken token)
    {
        var nodes = await _context.WorkerNodes
            .Where(n => n.Status == NodeStatus.Online && (n.Capabilities & NodeCapability.Kvm) != 0 && !n.IsLocal)
            .ToListAsync(token);

        foreach (var node in nodes)
        {
            try
            {
                _logger.LogInformation("Distributing image {ImageId} to node {NodeId}", template.Id, node.Id);
                await _agentClient.DownloadVmImageAsync(node.Id, template.ImageHash ?? "", token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to distribute image {ImageId} to node {NodeId}", template.Id, node.Id);
            }
        }
    }
}
