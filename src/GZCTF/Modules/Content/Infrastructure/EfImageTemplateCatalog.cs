using GZCTF.Models;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class EfImageTemplateCatalog(
    AppDbContext context,
    IImageTemplateArtifactCleaner artifactCleaner) : IImageTemplateCatalog
{
    public Task<ImageTemplateDescriptor?> FindAsync(int id, CancellationToken cancellationToken) =>
        context.ImageTemplates.AsNoTracking()
            .Where(template => template.Id == id)
            .Select(template => new ImageTemplateDescriptor(
                template.Id, template.CreatedById, template.Name))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ImageTemplateDetails?> FindDetailsAsync(int id, CancellationToken cancellationToken) =>
        context.ImageTemplates.AsNoTracking()
            .Where(template => template.Id == id)
            .Select(template => new ImageTemplateDetails(
                template.Id,
                template.CreatedById,
                template.Name,
                template.OSType,
                template.ImageType,
                template.Status,
                template.RegistryUrl,
                template.FileSize,
                template.Description,
                template.ErrorMessage,
                template.ImageHash,
                template.VmArtifactStatus,
                template.VmRuntimeMode,
                template.VmNetworkMode,
                template.UploadedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ImageTemplateDeleteDecision> MarkDeletingAsync(
        int id,
        Func<CancellationToken, Task<ImageTemplateDeleteDecision>> checkReferences,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (context.Database.IsRelational())
            transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        await using var transactionScope = transaction;
        var template = await context.ImageTemplates.SingleOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (template is null)
        {
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new ImageTemplateDeleteDecision(true, []);
        }

        if (template.Status == ImageStatus.Deleting)
        {
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new ImageTemplateDeleteDecision(true, []);
        }

        var decision = await checkReferences(cancellationToken);
        if (!decision.Allowed)
        {
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return decision;
        }

        template.Status = ImageStatus.Deleting;
        template.ErrorMessage = null;
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return decision;
    }

    public async Task CompleteDeletionAsync(int id, CancellationToken cancellationToken)
    {
        var template = await context.ImageTemplates
            .Include(item => item.PreparedArtifact)
            .SingleOrDefaultAsync(
            item => item.Id == id && item.Status == ImageStatus.Deleting,
            cancellationToken);
        if (template is null)
            return;

        try
        {
            await artifactCleaner.CleanupAsync(template, cancellationToken);
            var preparedArtifact = template.PreparedArtifact;
            context.ImageTemplates.Remove(template);
            if (preparedArtifact is not null)
                context.VmPreparedArtifacts.Remove(preparedArtifact);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            template.ErrorMessage = TrimError(exception.Message);
            await context.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static string TrimError(string message) =>
        message.Length <= 1024 ? message : message[..1024];
}
