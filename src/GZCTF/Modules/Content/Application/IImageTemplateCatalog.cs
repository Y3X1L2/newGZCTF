using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Content.Application;

public interface IImageTemplateCatalog
{
    Task<ImageTemplateDescriptor?> FindAsync(int id, CancellationToken cancellationToken);
    Task<ImageTemplateDetails?> FindDetailsAsync(int id, CancellationToken cancellationToken);
    Task<ImageTemplateDeleteDecision> MarkDeletingAsync(
        int id,
        Func<CancellationToken, Task<ImageTemplateDeleteDecision>> checkReferences,
        CancellationToken cancellationToken);
    Task CompleteDeletionAsync(int id, CancellationToken cancellationToken);
}

public sealed class ImageTemplateDeletionService(
    IImageTemplateCatalog catalog,
    ImageTemplateReferenceService references)
{
    public async Task<ImageTemplateDeleteResult> DeleteAsync(
        int id,
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        var template = await catalog.FindAsync(id, cancellationToken);
        if (template is null)
            return new ImageTemplateDeleteResult(ImageTemplateDeleteStatus.NotFound, []);

        var canDelete = actor.Role >= Role.Admin ||
                        template.CreatedById.HasValue && template.CreatedById == actor.UserId;
        if (!canDelete)
            return new ImageTemplateDeleteResult(ImageTemplateDeleteStatus.Forbidden, []);

        var decision = await catalog.MarkDeletingAsync(
            id,
            token => references.CanDeleteAsync(id, token),
            cancellationToken);
        if (!decision.Allowed)
            return new ImageTemplateDeleteResult(ImageTemplateDeleteStatus.InUse, decision.References);

        await catalog.CompleteDeletionAsync(id, cancellationToken);
        return new ImageTemplateDeleteResult(ImageTemplateDeleteStatus.Deleted, []);
    }
}
