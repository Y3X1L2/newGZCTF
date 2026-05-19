using GZCTF.Models;
using GZCTF.Models.Data;

namespace GZCTF.Services.Fleet;

public class ImageDistributionService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ImageDistributionService> _logger;

    public ImageDistributionService(AppDbContext context, ILogger<ImageDistributionService> logger)
    { _context = context; _logger = logger; }

    public async Task DistributeToCapableNodesAsync(ImageTemplate template, CancellationToken token)
    {
        var nodes = _context.WorkerNodes.Where(n => n.Status == NodeStatus.Online && (n.Capabilities & NodeCapability.Kvm) != 0).ToList();
        foreach (var node in nodes)
        {
            _logger.LogInformation("Distributing image {Image} to node {Node}", template.Id, node.Id);
            // Agent will check SHA256 hash and pull if needed
            var target = new DeploymentTarget
            {
                TargetNodeId = node.Id, Type = TargetType.Vm,
                Action = TargetAction.Create,
                Payload = System.Text.Json.JsonSerializer.Serialize(new { imageId = template.Id, localPath = template.LocalFilePath })
            };
            _context.DeploymentTargets.Add(target);
        }
        await _context.SaveChangesAsync(token);
    }
}
