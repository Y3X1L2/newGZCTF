using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class EfImageImportTemplateStore(AppDbContext context) : IImageImportTemplateStore
{
    public async Task<ImageTemplateDescriptor> MaterializeAsync(
        ImageImportJob job,
        ImageImportArtifact artifact,
        bool persistJobLink,
        CancellationToken cancellationToken)
    {
        if (!job.CreatedById.HasValue)
            throw new ApiOperationTerminalException(
                "image_owner_missing", "The image owner no longer exists.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"image-import:{job.CreatedById.Value:N}:{job.RequestedName}";
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);

        var existing = await context.ImageTemplates.SingleOrDefaultAsync(
            template => template.CreatedById == job.CreatedById &&
                        template.Name == job.RequestedName,
            cancellationToken);
        if (existing is not null)
        {
            var sameSource = await context.ImageImportJobs.AsNoTracking().AnyAsync(
                item => item.ImageTemplateId == existing.Id &&
                        item.SourceReference == job.SourceReference,
                cancellationToken);
            if (!sameSource && existing.Status != ImageStatus.Error)
                throw new ApiOperationTerminalException(
                    "image_template_conflict",
                    "An image template with the same name already exists for this owner.");
        }

        var template = existing ?? new ImageTemplate
        {
            Name = job.RequestedName,
            OSType = job.RequestedOsType,
            ImageType = job.RequestedTemplateKind,
            CreatedById = job.CreatedById
        };
        template.Name = job.RequestedName;
        template.OSType = job.RequestedOsType;
        template.ImageType = job.RequestedTemplateKind;
        template.RegistryUrl = artifact.RegistryUrl;
        template.RegistryAuth = null;
        template.ImageHash = artifact.ImageHash;
        template.FileSize = artifact.ContentLength;
        template.Description = artifact.Description;
        template.OriginalArchiveName = job.OriginalFileName;
        template.Status = ImageStatus.Ready;
        template.ErrorMessage = null;
        template.UploadedAt = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            context.ImageTemplates.Add(template);
            await context.SaveChangesAsync(cancellationToken);
        }

        if (persistJobLink)
        {
            job.ImageTemplateId = template.Id;
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (existing is not null)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ImageTemplateDescriptor(template.Id, template.CreatedById, template.Name);
    }
}
