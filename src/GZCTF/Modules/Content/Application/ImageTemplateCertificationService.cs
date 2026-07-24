using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.Content.Application;

public sealed class ImageTemplateCertificationService(
    AppDbContext context,
    ExternalApiAuditContext auditContext)
{
    public const string OperationKind = "image-template.certify";
    public const string RouteKey = "POST:/api/open/v1/images/{imageTemplateId}/certifications";

    public async Task<IdempotencyBeginResult> SubmitAsync(
        Guid apiTokenId,
        ActorContext actor,
        int imageTemplateId,
        string idempotencyKey,
        ImageTemplateCertificationRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = actor.UserId ?? throw new ImageTemplateCertificationContractException(
            "authentication_required", "Authentication is required.", 401);
        var template = await context.ImageTemplates.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == imageTemplateId && item.Status == ImageStatus.Ready, cancellationToken)
            ?? throw new ImageTemplateCertificationContractException(
                "image_not_found", "Image template was not found or is not ready.", 404);
        if (string.IsNullOrWhiteSpace(template.ImageHash))
            throw new ImageTemplateCertificationContractException(
                "image_digest_missing", "Image template has no immutable image digest.", 409);
        var capabilities = NormalizeCapabilities(request.Capabilities, template);
        var probeKind = request.ProbeKind.Trim().ToLowerInvariant();
        if (probeKind is not "external-evidence" and not "controlled-probe")
            throw new ImageTemplateCertificationContractException(
                "certification_probe_kind_invalid", "ProbeKind is invalid.", 400);
        var evidence = probeKind == "external-evidence"
            ? NormalizeEvidenceDigest(request.EvidenceDigest)
            : null;
        if (probeKind == "controlled-probe" && !string.IsNullOrWhiteSpace(request.EvidenceDigest))
            throw new ImageTemplateCertificationContractException(
                "certification_evidence_not_allowed",
                "Controlled probes generate their own evidence and do not accept EvidenceDigest.", 400);
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            imageTemplateId,
            imageHash = template.ImageHash,
            capabilities,
            evidence,
            probeKind
        })));
        var key = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var existing = await FindOperationAsync(apiTokenId, key, cancellationToken);
        if (existing is not null) return Reuse(existing, requestHash);
        var operation = new ApiOperation
        {
            Kind = OperationKind,
            ActorUserId = actorId,
            ApiTokenId = apiTokenId,
            RouteKey = RouteKey,
            IdempotencyKey = key,
            RequestHash = requestHash
        };
        var job = new ImageTemplateCertificationJob
        {
            OperationId = operation.Id,
            ImageTemplateId = imageTemplateId,
            CapabilitiesJson = JsonSerializer.Serialize(capabilities),
            EvidenceDigest = evidence,
            ProbeKind = probeKind,
            ActorUserId = actorId
        };
        context.AddRange(operation, job);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            existing = await FindOperationAsync(apiTokenId, key, cancellationToken);
            if (existing is null) throw;
            return Reuse(existing, requestHash);
        }
        auditContext.SetOperation(operation.Id, false);
        return new IdempotencyBeginResult(operation, false);
    }

    public async Task<IReadOnlyList<ImageTemplateCertificationModel>> ListAsync(
        int imageTemplateId,
        CancellationToken cancellationToken) =>
        (await context.ImageTemplateCapabilityCertifications.AsNoTracking()
            .Where(item => item.ImageTemplateId == imageTemplateId)
            .OrderByDescending(item => item.CertifiedAt)
            .ToArrayAsync(cancellationToken))
        .Select(ToModel).ToArray();

    public static ImageTemplateCertificationModel ToModel(ImageTemplateCapabilityCertification item) => new(
        item.Id,
        item.ImageTemplateId,
        item.ImageHash,
        item.Status,
        JsonSerializer.Deserialize<string[]>(item.CapabilitiesJson) ?? [],
        item.EvidenceDigest,
        item.ProbeKind,
        item.WorkerNodeId,
        item.ErrorCode,
        item.ErrorDetail,
        item.DomainCreateDurationMs,
        item.GuestReadyDurationMs,
        item.FullProbeDurationMs,
        item.PreparationContractVersion,
        item.GuestProtocolVersion,
        item.CertifiedAt);

    private Task<ApiOperation?> FindOperationAsync(Guid tokenId, string key, CancellationToken cancellationToken) =>
        context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ApiTokenId == tokenId && item.RouteKey == RouteKey && item.IdempotencyKey == key,
            cancellationToken);

    private IdempotencyBeginResult Reuse(ApiOperation operation, string requestHash)
    {
        if (!string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException();
        auditContext.SetOperation(operation.Id, true);
        return new IdempotencyBeginResult(operation, true);
    }

    private static string[] NormalizeCapabilities(
        IReadOnlyList<string> values,
        ImageTemplate template)
    {
        var capabilities = values.Select(item => item.Trim()).Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (capabilities.Length == 0 || capabilities.Any(item => !ImageTemplateCapabilityIds.All.Contains(item)))
            throw new ImageTemplateCertificationContractException(
                "certification_capability_invalid", "Certification contains an unknown capability.", 400);
        if (template.OSType == OSType.Linux && capabilities.Any(item =>
                item is ImageTemplateCapabilityIds.WindowsPowerShell or ImageTemplateCapabilityIds.WindowsCloudbaseInit))
            throw new ImageTemplateCertificationContractException(
                "certification_os_mismatch", "Windows capabilities cannot certify a Linux template.", 409);
        if (template.OSType == OSType.Windows && capabilities.Any(item =>
                item is ImageTemplateCapabilityIds.LinuxCloudInitNoCloud))
            throw new ImageTemplateCertificationContractException(
                "certification_os_mismatch", "Linux capabilities cannot certify a Windows template.", 409);
        return capabilities;
    }

    private static string NormalizeEvidenceDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ImageTemplateCertificationContractException(
                "certification_evidence_invalid", "External evidence requires a SHA-256 digest.", 400);
        value = value.Trim().ToLowerInvariant();
        if (value.StartsWith("sha256:", StringComparison.Ordinal)) value = value[7..];
        if (value.Length != 64 || value.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ImageTemplateCertificationContractException(
                "certification_evidence_invalid", "EvidenceDigest must be a sha256 digest.", 400);
        return value;
    }
}

public sealed class ImageTemplateCertificationContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);
