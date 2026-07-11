using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Request.Game;
using GZCTF.Modules.Content.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Infrastructure;

public sealed class PenetrationImageTemplateReferenceProvider(AppDbContext context)
    : IImageTemplateReferenceProvider
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public string Module => "Penetration";

    public async Task<IReadOnlyList<ImageTemplateReference>> GetReferencesAsync(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var nodes = await context.PenetrationNodes.AsNoTracking()
            .Where(node => node.ImageTemplateId == imageTemplateId)
            .Select(node => new { node.Id, node.Name })
            .ToArrayAsync(cancellationToken);
        var snapshots = await context.PenetrationPublishedSnapshots.AsNoTracking()
            .Select(snapshot => new
            {
                snapshot.GameId,
                snapshot.PublishedVersion,
                snapshot.SnapshotJson
            })
            .ToArrayAsync(cancellationToken);

        List<ImageTemplateReference> references =
        [
            .. nodes.Select(node => new ImageTemplateReference(
                Module, "topology-node", node.Id.ToString(), node.Name))
        ];

        foreach (var snapshot in snapshots)
        {
            var resourceId = $"{snapshot.GameId}:{snapshot.PublishedVersion}";
            PenetrationConfigModel? model;
            try
            {
                using var document = JsonDocument.Parse(snapshot.SnapshotJson);
                model = HasNodesArray(document.RootElement)
                    ? JsonSerializer.Deserialize<PenetrationConfigModel>(
                        snapshot.SnapshotJson,
                        SnapshotJsonOptions)
                    : null;
            }
            catch (JsonException)
            {
                references.Add(new ImageTemplateReference(
                    Module,
                    "published-snapshot-invalid",
                    resourceId,
                    $"Invalid published snapshot for game {snapshot.GameId} v{snapshot.PublishedVersion}"));
                continue;
            }

            if (model?.Nodes is null)
            {
                references.Add(new ImageTemplateReference(
                    Module,
                    "published-snapshot-invalid",
                    resourceId,
                    $"Invalid published snapshot for game {snapshot.GameId} v{snapshot.PublishedVersion}"));
                continue;
            }

            if (model.Nodes.Any(node => node.ImageTemplateId == imageTemplateId))
                references.Add(new ImageTemplateReference(
                    Module,
                    "published-snapshot",
                    resourceId,
                    $"Published snapshot for game {snapshot.GameId} v{snapshot.PublishedVersion}"));
        }

        return references
            .DistinctBy(reference => (reference.Module, reference.ResourceType, reference.ResourceId))
            .OrderBy(reference => reference.ResourceType, StringComparer.Ordinal)
            .ThenBy(reference => reference.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasNodesArray(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.EnumerateObject().Any(property =>
            property.Name.Equals("nodes", StringComparison.OrdinalIgnoreCase) &&
            property.Value.ValueKind == JsonValueKind.Array);
}
