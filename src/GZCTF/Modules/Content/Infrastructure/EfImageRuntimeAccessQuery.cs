using GZCTF.Models;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class EfImageRuntimeAccessQuery(AppDbContext context) : IImageRuntimeAccessQuery
{
    public async Task<IReadOnlyDictionary<int, ImageRuntimeAccessSummary>> GetBatchAsync(
        IReadOnlyCollection<int> templateIds, CancellationToken cancellationToken)
    {
        var ids = templateIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<int, ImageRuntimeAccessSummary>();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new { item.Id, item.ImageHash, item.OSType })
            .ToArrayAsync(cancellationToken);
        var access = await context.ImageTemplateRemoteAccesses.AsNoTracking()
            .Where(item => ids.Contains(item.ImageTemplateId))
            .Select(item => new
            {
                item.ImageTemplateId, item.Enabled, item.Protocol, item.Port,
                HasUsername = item.Username != null && item.Username.Trim() != "",
                HasSecret = item.ProtectedSecret != null && item.ProtectedSecret.Trim() != ""
            })
            .ToDictionaryAsync(item => item.ImageTemplateId, cancellationToken);
        return templates.ToDictionary(item => item.Id, item =>
        {
            var configuration = access.GetValueOrDefault(item.Id);
            return new ImageRuntimeAccessSummary(
                item.Id, item.ImageHash, item.OSType,
                configuration is { Enabled: true } ? configuration.Protocol.ToString().ToLowerInvariant() : null,
                configuration is { Enabled: true, Port: >= 1 and <= 65535,
                    HasUsername: true, HasSecret: true });
        });
    }
}
