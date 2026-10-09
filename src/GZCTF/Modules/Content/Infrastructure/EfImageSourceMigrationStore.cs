using System.Security.Cryptography;
using System.Text;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class EfImageSourceMigrationStore(AppDbContext context, VmSourceMigrationFiles files,
    VmImageRegistryService registry) : IImageSourceMigrationStore
{
    public async Task<IdempotencyBeginResult> SubmitAsync(int templateId, Guid actorId, string idempotencyKey,
        CancellationToken token)
    {
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(token) : null;
        await LockAsync(context, templateId, token);
        var route = $"POST:image-template:{templateId}:source-migration";
        var key = idempotencyKey.Trim();
        var existing = await context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Kind == ImageSourceMigrationService.OperationKind && item.ActorUserId == actorId &&
            item.RouteKey == route && item.IdempotencyKey == key, token);
        if (existing is not null) return new(existing, true);
        var template = await context.ImageTemplates.Include(item => item.PreparedArtifact)
            .SingleOrDefaultAsync(item => item.Id == templateId, token)
            ?? throw new ImageSourceMigrationContractException("image_not_found", "Image template was not found.", 404);
        if (template.ImageType != ImageType.Qcow2 || template.Status != ImageStatus.Ready ||
            string.IsNullOrWhiteSpace(template.ImageHash) || template.FileSize <= 0)
            throw new ImageSourceMigrationContractException("source_migration_template_invalid",
                "Source migration requires a ready qcow2 template with a fixed SHA-256 and size.", 409);

        var operationId = Guid.CreateVersion7();
        string reference;
        if (!string.IsNullOrWhiteSpace(template.LocalFilePath))
        {
            if (template.PreparedArtifactId.HasValue || template.VmArtifactStatus != VmArtifactStatus.None)
                throw new ImageSourceMigrationContractException("source_migration_not_legacy",
                    "This template already has a prepared artifact and requires a separate source review.", 409);
            (string Path, string Identity) file;
            try { file = files.Capture(template.LocalFilePath); }
            catch (ApiOperationTerminalException exception)
            { throw new ImageSourceMigrationContractException(exception.Code, exception.Message, 409); }
            catch (FileNotFoundException)
            { throw new ImageSourceMigrationContractException("source_migration_source_missing", "Legacy source file is unavailable.", 409); }
            var target = registry.BuildReference(template);
            reference = new VmSourceMigrationReference(file.Path, file.Identity,
                target.RegistryAddress, target.Repository, target.Tag, operationId).Encode();
        }
        else
        {
            // A new explicit request can retry cleanup after the bounded worker attempts failed.
            var previous = await (from priorJob in context.ImageImportJobs.AsNoTracking()
                join priorOperation in context.ApiOperations.AsNoTracking() on priorJob.OperationId equals priorOperation.Id
                where priorJob.ImageTemplateId == templateId && priorOperation.Kind == ImageSourceMigrationService.OperationKind &&
                      priorJob.ExpectedDigest == template.ImageHash && priorJob.ContentLength == template.FileSize
                orderby priorJob.CreatedAt descending select priorJob).FirstOrDefaultAsync(token)
                ?? throw new ImageSourceMigrationContractException("source_migration_no_captured_source",
                    "There is no legacy source or recoverable source-migration checkpoint.", 409);
            reference = previous.SourceReference;
        }
        var now = DateTimeOffset.UtcNow;
        var operation = new ApiOperation
        {
            Id = operationId,
            Kind = ImageSourceMigrationService.OperationKind, ActorUserId = actorId, RouteKey = route,
            IdempotencyKey = key, RequestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(route))),
            ResourceType = "image-template", ResourceId = templateId.ToString(), Stage = "source-captured",
            CreatedAt = now, UpdatedAt = now
        };
        var job = new ImageImportJob
        {
            OperationId = operation.Id, SourceKind = ImageImportSourceKind.VmQcow2,
            SourceReference = reference, StagedPath = null, ImageTemplateId = templateId,
            ExpectedDigest = template.ImageHash, ContentLength = template.FileSize,
            OriginalFileName = Path.GetFileName(template.LocalFilePath), RequestedName = template.Name,
            RequestedTemplateKind = template.ImageType, RequestedOsType = template.OSType,
            RequestedVmNetworkMode = template.VmNetworkMode, CreatedById = actorId, CreatedAt = now
        };
        context.AddRange(operation, job);
        await context.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return new(operation, false);
    }

    public Task<ApiOperation?> FindAsync(int templateId, Guid operationId, CancellationToken token) =>
        (from operation in context.ApiOperations.AsNoTracking()
         join job in context.ImageImportJobs.AsNoTracking() on operation.Id equals job.OperationId
         where operation.Id == operationId && operation.Kind == ImageSourceMigrationService.OperationKind &&
               job.ImageTemplateId == templateId select operation).SingleOrDefaultAsync(token);

    internal static Task LockAsync(AppDbContext context, int templateId, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({$"image-source-migration:{templateId}"}, 0))", token)
            : Task.CompletedTask;
}
