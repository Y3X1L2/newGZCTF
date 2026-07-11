using GZCTF.Modules.Content.Contracts;

namespace GZCTF.Modules.Content.Application;

public sealed class ImageTemplateReferenceService(
    IEnumerable<IImageTemplateReferenceProvider> providers)
{
    private readonly IReadOnlyList<IImageTemplateReferenceProvider> _providers =
        providers.OrderBy(provider => provider.Module, StringComparer.Ordinal).ToArray();

    public async Task<ImageTemplateDeleteDecision> CanDeleteAsync(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        List<ImageTemplateReference> references = [];
        foreach (var provider in _providers)
            references.AddRange(await provider.GetReferencesAsync(imageTemplateId, cancellationToken));

        var orderedReferences = references
            .OrderBy(item => item.Module, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceType, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId, StringComparer.Ordinal)
            .ToArray();
        return new ImageTemplateDeleteDecision(orderedReferences.Length == 0, orderedReferences);
    }
}
