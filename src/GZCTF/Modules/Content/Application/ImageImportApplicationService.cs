using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Content.Application;

public sealed record ImageImportSubmission(
    Guid ApiTokenId,
    Guid ActorUserId,
    string RouteKey,
    string IdempotencyKey,
    string RequestHash,
    ImageImportJob Job);

public sealed record StagedImageImport(
    string Path,
    string OriginalFileName,
    long ContentLength,
    string ContentDigest);

public interface IImageImportStagingStore
{
    Task<StagedImageImport> StageAsync(
        Stream source,
        string originalFileName,
        long declaredLength,
        string? expectedDigest,
        CancellationToken cancellationToken);

    Task VerifyAsync(ImageImportJob job, CancellationToken cancellationToken);

    Task DeleteAsync(string? path, CancellationToken cancellationToken);

    Task<int> DeleteUnreferencedAsync(
        IReadOnlySet<string> activePaths,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken);
}

public interface IImageImportSubmissionStore
{
    Task<IdempotencyBeginResult> SubmitAsync(
        ImageImportSubmission submission,
        CancellationToken cancellationToken);
}

public interface IImageImportExecutor
{
    Task<ImageImportArtifact> ImportDockerReferenceAsync(
        ImageImportJob job,
        CancellationToken cancellationToken);

    Task<ImageImportArtifact> ImportDockerArchiveAsync(
        ImageImportJob job,
        CancellationToken cancellationToken);
}

public interface IImageImportTemplateStore
{
    Task<ImageTemplateDescriptor> MaterializeAsync(
        ImageImportJob job,
        ImageImportArtifact artifact,
        bool persistJobLink,
        CancellationToken cancellationToken);
}

public sealed class ImageImportApplicationService(
    IImageImportSubmissionStore store,
    IImageImportExecutor executor,
    IImageImportTemplateStore templates,
    IImageImportStagingStore staging,
    DockerImageReferencePolicy referencePolicy)
{
    public const string OperationKind = "image.import";
    public const string DockerReferenceRouteKey = "POST:/api/open/v1/images/docker-references";
    public const string DockerArchiveRouteKey = "POST:/api/open/v1/images/docker-archives";

    public async Task<IdempotencyBeginResult> SubmitDockerReferenceAsync(
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        DockerImageReferenceImportCommand command,
        CancellationToken cancellationToken)
    {
        if (!actor.UserId.HasValue)
            throw new ImageImportContractException(
                "authentication_required", "Authentication is required.", 401);
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);

        var normalized = Normalize(command);
        await referencePolicy.ValidateAsync(normalized.RegistryUrl, cancellationToken);
        var requestHash = ComputeRequestHash(normalized);
        var job = new ImageImportJob
        {
            SourceKind = ImageImportSourceKind.DockerReference,
            SourceReference = normalized.RegistryUrl,
            ExpectedDigest = normalized.ExpectedDigest,
            RequestedTemplateKind = ImageType.Docker,
            RequestedOsType = normalized.OSType,
            RequestedName = normalized.Name,
            CreatedById = actor.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return await store.SubmitAsync(
            new ImageImportSubmission(
                apiTokenId,
                actor.UserId.Value,
                DockerReferenceRouteKey,
                normalizedKey,
                requestHash,
                job),
            cancellationToken);
    }

    public async Task<IdempotencyBeginResult> SubmitDockerArchiveAsync(
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        Stream source,
        string originalFileName,
        long contentLength,
        DockerImageArchiveImportCommand command,
        CancellationToken cancellationToken)
    {
        if (!actor.UserId.HasValue)
            throw new ImageImportContractException(
                "authentication_required", "Authentication is required.", 401);

        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var normalized = Normalize(command);
        var staged = await staging.StageAsync(
            source,
            originalFileName,
            contentLength,
            normalized.ExpectedDigest,
            cancellationToken);
        var requestHash = ComputeRequestHash(normalized, staged.ContentDigest);
        var job = CreateArchiveJob(actor.UserId.Value, normalized, staged);

        try
        {
            var result = await store.SubmitAsync(
                new ImageImportSubmission(
                    apiTokenId,
                    actor.UserId.Value,
                    DockerArchiveRouteKey,
                    normalizedKey,
                    requestHash,
                    job),
                cancellationToken);
            if (result.Reused)
                await staging.DeleteAsync(staged.Path, cancellationToken);
            return result;
        }
        catch (IdempotencyConflictException)
        {
            await staging.DeleteAsync(staged.Path, CancellationToken.None);
            throw;
        }
    }

    public async Task<ImageTemplateDescriptor> ImportDockerReferenceNowAsync(
        ActorContext actor,
        DockerImageReferenceImportCommand command,
        CancellationToken cancellationToken)
    {
        if (!actor.UserId.HasValue)
            throw new ImageImportContractException(
                "authentication_required", "Authentication is required.", 401);

        var normalized = Normalize(command);
        var job = new ImageImportJob
        {
            SourceKind = ImageImportSourceKind.DockerReference,
            SourceReference = normalized.RegistryUrl,
            ExpectedDigest = normalized.ExpectedDigest,
            RequestedTemplateKind = ImageType.Docker,
            RequestedOsType = normalized.OSType,
            RequestedName = normalized.Name,
            CreatedById = actor.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        return await ExecuteJobAsync(job, false, cancellationToken);
    }

    public async Task<ImageTemplateDescriptor> ImportDockerArchiveNowAsync(
        ActorContext actor,
        Stream source,
        string originalFileName,
        long contentLength,
        DockerImageArchiveImportCommand command,
        CancellationToken cancellationToken)
    {
        if (!actor.UserId.HasValue)
            throw new ImageImportContractException(
                "authentication_required", "Authentication is required.", 401);

        var normalized = Normalize(command);
        var staged = await staging.StageAsync(
            source,
            originalFileName,
            contentLength,
            normalized.ExpectedDigest,
            cancellationToken);
        var job = CreateArchiveJob(actor.UserId.Value, normalized, staged);
        try
        {
            return await ExecuteJobAsync(job, false, cancellationToken);
        }
        finally
        {
            await staging.DeleteAsync(staged.Path, CancellationToken.None);
        }
    }

    public async Task<ImageTemplateDescriptor> ExecuteJobAsync(
        ImageImportJob job,
        bool persistJobLink,
        CancellationToken cancellationToken)
    {
        var artifact = job.SourceKind switch
        {
            ImageImportSourceKind.DockerReference =>
                await executor.ImportDockerReferenceAsync(job, cancellationToken),
            ImageImportSourceKind.DockerArchive =>
                await executor.ImportDockerArchiveAsync(job, cancellationToken),
            _ => throw new ApiOperationTerminalException(
                "image_source_unsupported", "The image import source is not supported.")
        };
        return await templates.MaterializeAsync(
            job, artifact, persistJobLink, cancellationToken);
    }

    private static ImageImportJob CreateArchiveJob(
        Guid actorUserId,
        DockerImageArchiveImportCommand command,
        StagedImageImport staged) => new()
    {
        SourceKind = ImageImportSourceKind.DockerArchive,
        SourceReference = command.SourceImage ?? string.Empty,
        StagedPath = staged.Path,
        OriginalFileName = staged.OriginalFileName,
        ContentLength = staged.ContentLength,
        ExpectedDigest = staged.ContentDigest,
        RequestedTemplateKind = ImageType.Docker,
        RequestedOsType = command.OSType,
        RequestedName = command.Name,
        CreatedById = actorUserId,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static DockerImageReferenceImportCommand Normalize(
        DockerImageReferenceImportCommand command)
    {
        var name = command.Name.Trim();
        var source = command.RegistryUrl.Trim();
        if (name.Length is < 1 or > 256)
            throw new ImageImportContractException(
                "image_name_invalid", "Image template name is invalid.", 400);
        if (source.Length is < 1 or > 512 || source.Any(char.IsWhiteSpace))
            throw new ImageImportContractException(
                "image_reference_invalid", "Docker image reference is invalid.", 400);

        var digest = NormalizeDigest(command.ExpectedDigest);

        return command with
        {
            Name = name,
            RegistryUrl = source,
            ExpectedDigest = digest
        };
    }

    private static DockerImageArchiveImportCommand Normalize(
        DockerImageArchiveImportCommand command)
    {
        var name = command.Name.Trim();
        var sourceImage = command.SourceImage?.Trim();
        if (name.Length is < 1 or > 256)
            throw new ImageImportContractException(
                "image_name_invalid", "Image template name is invalid.", 400);
        if (sourceImage is { Length: > 512 } || sourceImage?.Any(char.IsWhiteSpace) == true)
            throw new ImageImportContractException(
                "image_reference_invalid", "Docker source image reference is invalid.", 400);

        return command with
        {
            Name = name,
            SourceImage = string.IsNullOrWhiteSpace(sourceImage) ? null : sourceImage,
            ExpectedDigest = NormalizeDigest(command.ExpectedDigest)
        };
    }

    private static string ComputeRequestHash(DockerImageReferenceImportCommand command)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(command);
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private static string ComputeRequestHash(
        DockerImageArchiveImportCommand command,
        string contentDigest)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { command, contentDigest });
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private static string NormalizeIdempotencyKey(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 128)
            throw new IdempotencyValidationException(
                string.IsNullOrEmpty(normalized)
                    ? "idempotency_key_required"
                    : "idempotency_key_invalid",
                string.IsNullOrEmpty(normalized)
                    ? "An Idempotency-Key header is required."
                    : "Idempotency-Key cannot exceed 128 characters.");
        return normalized;
    }

    private static string? NormalizeDigest(string? value)
    {
        var digest = value?.Trim().ToLowerInvariant();
        if (digest?.StartsWith("sha256:", StringComparison.Ordinal) == true)
            digest = digest[7..];
        if (digest is { Length: > 0 } &&
            (digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character))))
            throw new ImageImportContractException(
                "image_digest_invalid", "Expected digest must be a SHA-256 digest.", 400);
        return string.IsNullOrEmpty(digest) ? null : digest;
    }
}

public sealed class ImageImportContractException(
    string code,
    string message,
    int statusCode) : ApiContractException(code, message, statusCode);

public sealed class ImageImportNotFoundException()
    : ApiOperationTerminalException(
        "image_import_job_not_found",
        "The durable image import job was not found.");
