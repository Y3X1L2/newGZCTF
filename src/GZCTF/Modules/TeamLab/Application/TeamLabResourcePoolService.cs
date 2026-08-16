using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Read-only resource pool projection. It aggregates existing facts (nodes,
/// templates, distribution cache) without duplicating module ownership and
/// never exposes execution-plane addresses or registry credentials.
/// </summary>
public sealed class TeamLabResourcePoolService(AppDbContext context)
{
    public async Task<TeamLabResourcePoolSnapshotModel> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var nodes = await ListComputeNodesAsync(cancellationToken);
        var templates = await ListTemplatesAsync(cancellationToken);
        return new TeamLabResourcePoolSnapshotModel(nodes, templates);
    }

    public async Task<IReadOnlyList<TeamLabComputeNodePoolModel>> ListComputeNodesAsync(
        CancellationToken cancellationToken)
    {
        var nodes = await context.WorkerNodes.AsNoTracking()
            .OrderBy(node => node.Name)
            .ToArrayAsync(cancellationToken);
        return nodes.Select(ToModel).ToArray();
    }

    public async Task<IReadOnlyList<TeamLabTemplatePoolModel>> ListTemplatesAsync(
        CancellationToken cancellationToken)
    {
        var templates = await context.ImageTemplates.AsNoTracking()
            .OrderBy(template => template.Id)
            .ToArrayAsync(cancellationToken);
        return templates.Select(ToModel).ToArray();
    }

    public async Task<TeamLabNodeCachePageModel> ListNodeCacheAsync(
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var cursor = DecodeCursor(after);
        var take = Math.Clamp(limit, 1, 100);
        IQueryable<ImageDistributionRecord> query = context.ImageDistributionRecords.AsNoTracking()
            .Include(record => record.References);
        if (cursor is not null)
            query = query.Where(record => record.Id.CompareTo(cursor.Value) > 0);
        var records = await query
            .OrderBy(record => record.Id)
            .Take(take + 1)
            .ToArrayAsync(cancellationToken);
        return new TeamLabNodeCachePageModel(
            records.Take(take).Select(ToModel).ToArray(),
            records.Length > take ? EncodeCursor(records[take - 1].Id) : null);
    }

    private static TeamLabComputeNodePoolModel ToModel(WorkerNode node) => new(
        node.Id,
        node.Name,
        node.Status.ToString().ToLowerInvariant(),
        node.IsSchedulable,
        node.Capabilities.HasFlag(NodeCapability.Docker),
        node.Capabilities.HasFlag(NodeCapability.Kvm),
        node.TeamLabNetworkEnabled,
        node.TeamLabFabricStatus.ToString().ToLowerInvariant(),
        node.CurrentContainers,
        node.MaxContainers,
        node.CurrentVms,
        node.MaxVms,
        Math.Round(node.CpuLoad, 2),
        Math.Round(node.MemoryLoad, 2),
        node.AgentVersion,
        node.LastHeartbeat,
        node.LiveMetricObservedAt);

    private static TeamLabTemplatePoolModel ToModel(ImageTemplate template) => new(
        template.Id,
        template.Name,
        template.OSType.ToString().ToLowerInvariant(),
        template.ImageType.ToString().ToLowerInvariant(),
        template.Status.ToString().ToLowerInvariant(),
        template.FileSize,
        template.ImageHash,
        template.SupportsInstanceCredentials,
        template.UploadedAt);

    private static TeamLabNodeCachePoolModel ToModel(ImageDistributionRecord record) => new(
        record.ImageTemplateId,
        record.WorkerNodeId,
        string.IsNullOrWhiteSpace(record.ImageHash) ? null : record.ImageHash,
        record.Status.ToString().ToLowerInvariant(),
        record.Operation.ToString().ToLowerInvariant(),
        record.Stage.ToString().ToLowerInvariant(),
        record.AttemptCount,
        record.References.Count,
        string.IsNullOrWhiteSpace(record.LastErrorCode) ? null : record.LastErrorCode,
        record.ProgressUpdatedAt);

    private static Guid? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == 16 ? new Guid(bytes) : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new TeamLabApiContractException("node_cache_cursor_invalid", "节点缓存 cursor 无效", 400);
        }
    }

    private static string EncodeCursor(Guid id) => Convert.ToBase64String(id.ToByteArray());
}
