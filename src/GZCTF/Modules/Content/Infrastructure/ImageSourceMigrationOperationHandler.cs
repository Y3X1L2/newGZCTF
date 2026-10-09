using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class ImageSourceMigrationOperationHandler(AppDbContext context, ApiOperationService operations,
    VmSourceMigrationFiles files, VmImageRegistryService vmRegistry, OciArtifactRegistryClient registry)
    : IApiOperationHandler
{
    public string Kind => ImageSourceMigrationService.OperationKind;

    public async Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken token)
    {
        try { await MigrateAsync(operationId, leaseOwner, token); }
        catch (OciArtifactRegistryFailureException exception) when (exception.Retryable)
        { throw new ApiOperationRetryableException("source_migration_registry_unavailable", "Registry is temporarily unavailable; the captured source is retained."); }
        catch (OciArtifactRegistryFailureException)
        { throw new ApiOperationTerminalException("source_migration_registry_rejected", "Registry rejected the captured source; verify its access and identity before retrying."); }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.SecureConnectionError)
        { throw new ApiOperationTerminalException("source_migration_registry_tls_invalid", "Registry transport identity could not be verified; the captured source is retained."); }
        catch (HttpRequestException)
        { throw new ApiOperationRetryableException("source_migration_registry_unavailable", "Registry could not be reached; the captured source is retained."); }
        catch (IOException)
        { throw new ApiOperationRetryableException("source_migration_cleanup_unproven", "The captured source could not be read or proven unused; its bytes are retained."); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new ApiOperationRetryableException("source_migration_verification_timeout", "Source verification or local inventory timed out; the captured source is retained."); }
        catch (System.Text.Json.JsonException)
        { throw new ApiOperationTerminalException("source_migration_checkpoint_invalid", "Source checkpoint or Registry manifest is invalid; the captured source is retained."); }
    }

    async Task MigrateAsync(Guid operationId, string leaseOwner, CancellationToken token)
    {
        var job = await context.ImageImportJobs.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == operationId, token)
            ?? throw Rejected("Source migration checkpoint was not found.");
        if (!job.ImageTemplateId.HasValue || job.StagedPath is not null || string.IsNullOrWhiteSpace(job.ExpectedDigest))
            throw Rejected("Source migration checkpoint has an invalid identity.");
        var source = VmSourceMigrationReference.Decode(job.SourceReference);
        var reference = new OciArtifactReference(source.RegistryAddress, source.Repository, source.Tag,
            $"sha256:{OciArtifactRegistryClient.NormalizeDigest(job.ExpectedDigest)}", job.ContentLength);
        var template = await LoadAsync(job.ImageTemplateId.Value, token);
        CheckTemplate(template, job);
        if (template.LocalFilePath is not null)
        {
            if (!string.Equals(Path.GetFullPath(template.LocalFilePath), source.Path, PathComparison))
                throw Rejected("The template source changed after this operation captured it.");
            await files.VerifyAsync(source, job.ExpectedDigest, job.ContentLength, token);
            var destination = vmRegistry.BuildReference(template);
            if (destination.RegistryAddress != source.RegistryAddress || destination.Repository != source.Repository ||
                destination.Tag != source.Tag)
                throw Rejected("Registry settings changed before source migration completed.");
            await ProgressAsync(operationId, leaseOwner, "registry-verifying", 0, token);
            await vmRegistry.EnsureArtifactAsync(template, token);
        }
        else CheckPrepared(template, reference);
        await VerifyRegistryAsync(reference, token);

        await using (var transaction = context.Database.IsRelational()
                         ? await context.Database.BeginTransactionAsync(token) : null)
        {
            await EfImageSourceMigrationStore.LockAsync(context, template.Id, token);
            // Use fresh DB facts after the potentially long stream verification.
            context.ChangeTracker.Clear();
            template = await LoadAsync(job.ImageTemplateId.Value, token);
            CheckTemplate(template, job);
            if (template.LocalFilePath is not null)
            {
                if (!string.Equals(Path.GetFullPath(template.LocalFilePath), source.Path, PathComparison) ||
                    template.PreparedArtifactId.HasValue || template.VmArtifactStatus != VmArtifactStatus.None)
                    throw Rejected("The template source changed before registry registration.");
                var prepared = new VmPreparedArtifact
                {
                    OSType = template.OSType, Status = VmPreparedArtifactStatus.Ready,
                    ArtifactDigest = job.ExpectedDigest, ArtifactSize = job.ContentLength,
                    RegistryAddress = source.RegistryAddress, RegistryRepository = source.Repository, RegistryTag = source.Tag,
                    PreparedAt = DateTimeOffset.UtcNow, EvidenceDigest = null
                };
                context.VmPreparedArtifacts.Add(prepared);
                template.PreparedArtifact = prepared;
                template.VmArtifactStatus = VmArtifactStatus.Ready;
                template.LocalFilePath = null;
                await context.SaveChangesAsync(token);
            }
            else CheckPrepared(template, reference);
            if (transaction is not null) await transaction.CommitAsync(token);
        }
        await ProgressAsync(operationId, leaseOwner, "source-cleanup", 1, token);
        // Reconfirm DB binding and the entire Registry payload before relinquishing this file.
        context.ChangeTracker.Clear();
        template = await LoadAsync(job.ImageTemplateId.Value, token);
        CheckTemplate(template, job);
        CheckPrepared(template, reference);
        if (template.LocalFilePath is not null) throw Rejected("The template regained a local source during cleanup.");
        await VerifyRegistryAsync(reference, token);
        var otherLocalSources = await context.ImageTemplates.AsNoTracking()
            .Where(item => item.LocalFilePath != null).Select(item => item.LocalFilePath!).ToArrayAsync(token);
        if (otherLocalSources.Any(path => VmSourceMigrationFiles.SameEntry(source.Path, path)))
            throw new ApiOperationTerminalException("source_migration_source_still_referenced",
                "Another template still names this legacy source; its bytes are retained.");
        await files.DeleteAsync(operationId, source, job.ExpectedDigest, job.ContentLength, token);
        await ProgressAsync(operationId, leaseOwner, "source-migrated", 2, token);
    }

    async Task<ImageTemplate> LoadAsync(int id, CancellationToken token)
    {
        var templates = context.Database.CurrentTransaction is not null && context.Database.IsNpgsql()
            ? context.ImageTemplates.FromSqlInterpolated($"SELECT * FROM \"ImageTemplates\" WHERE \"Id\" = {id} FOR UPDATE")
            : context.ImageTemplates.Where(item => item.Id == id);
        return await templates.Include(item => item.PreparedArtifact).SingleOrDefaultAsync(token)
               ?? throw Rejected("The image template no longer exists.");
    }

    async Task VerifyRegistryAsync(OciArtifactReference reference, CancellationToken token)
    {
        try { await registry.VerifyBytesAsync(reference, token); }
        catch (OciArtifactRegistryFailureException) { throw; }
        catch (IOException)
        { throw new ApiOperationRetryableException("source_migration_registry_unavailable", "Registry payload could not be read completely; the captured source is retained."); }
        catch (InvalidOperationException)
        { throw new ApiOperationTerminalException("source_migration_registry_verification_failed", "Registry source SHA-256, size or manifest verification failed; the captured source is retained."); }
    }

    static void CheckTemplate(ImageTemplate template, ImageImportJob job)
    {
        if (template.Status != ImageStatus.Ready || template.ImageType != ImageType.Qcow2 ||
            template.ImageHash != job.ExpectedDigest || template.FileSize != job.ContentLength)
            throw Rejected("The image template identity changed during source migration.");
    }

    static void CheckPrepared(ImageTemplate template, OciArtifactReference reference)
    {
        if (template.VmArtifactStatus != VmArtifactStatus.Ready || template.PreparedArtifact is not { Status: VmPreparedArtifactStatus.Ready } prepared ||
            prepared.RegistryAddress != reference.RegistryAddress || prepared.RegistryRepository != reference.Repository ||
            prepared.RegistryTag != reference.Tag || prepared.ArtifactSize != reference.Size ||
            $"sha256:{OciArtifactRegistryClient.NormalizeDigest(prepared.ArtifactDigest)}" != reference.Digest)
            throw Rejected("The fixed Registry source does not match the captured migration checkpoint.");
    }

    async Task ProgressAsync(Guid operationId, string leaseOwner, string stage, long progress, CancellationToken token)
    {
        if (!await operations.UpdateProgressAsync(operationId, leaseOwner, stage, progress, 2,
                "image-template", null, null, token))
            throw new OperationCanceledException("Source migration lost its operation lease.", token);
    }
    static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    static ApiOperationTerminalException Rejected(string message) => new("source_migration_identity_changed", message);
}
