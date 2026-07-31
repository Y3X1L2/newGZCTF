using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class ImageTemplateCertificationOperationHandler(
    AppDbContext context,
    ApiOperationService operations,
    VmImageCertificationProbeService probeService) : IApiOperationHandler
{
    public string Kind => ImageTemplateCertificationService.OperationKind;

    public async Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        var job = await context.ImageTemplateCertificationJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken)
            ?? throw new ApiOperationTerminalException(
                "image_certification_job_not_found", "Image certification job was not found.");
        var template = await context.ImageTemplates
            .Include(item => item.PreparedArtifact)
            .SingleOrDefaultAsync(
            item => item.Id == job.ImageTemplateId && item.Status == ImageStatus.Ready,
            cancellationToken)
            ?? throw new ApiOperationTerminalException(
                "image_not_found", "Image template was not found or is not ready.");
        if (string.IsNullOrWhiteSpace(template.ImageHash))
            throw new ApiOperationTerminalException(
                "image_digest_missing", "Image template has no immutable image digest.");
        var capabilities = JsonSerializer.Deserialize<string[]>(job.CapabilitiesJson) ?? [];
        VmImageCertificationProbeResult? probe = null;
        string evidenceDigest;
        if (job.ProbeKind == "controlled-probe")
        {
            if (!await operations.UpdateProgressAsync(
                    operationId, leaseOwner, "probing", 0, 1,
                    "image-template-certification", template.Id.ToString(), null, cancellationToken))
                throw new OperationCanceledException(
                    "Image certification operation lease was lost.", cancellationToken);
            probe = await probeService.ProbeAsync(template, capabilities, cancellationToken);
            evidenceDigest = probe.EvidenceDigest;
            job.EvidenceDigest = evidenceDigest;
        }
        else
        {
            evidenceDigest = job.EvidenceDigest
                             ?? throw new ApiOperationTerminalException(
                                 "certification_evidence_missing", "External certification evidence is missing.");
        }
        var existing = await context.ImageTemplateCapabilityCertifications.SingleOrDefaultAsync(item =>
            item.ImageTemplateId == template.Id && item.ImageHash == template.ImageHash &&
            item.EvidenceDigest == evidenceDigest, cancellationToken);
        if (existing is null)
        {
            var stale = await context.ImageTemplateCapabilityCertifications.Where(item =>
                item.ImageTemplateId == template.Id && item.ImageHash != template.ImageHash &&
                item.Status == ImageTemplateCertificationStatus.Certified).ToArrayAsync(cancellationToken);
            foreach (var item in stale) item.Status = ImageTemplateCertificationStatus.Invalidated;
            existing = new ImageTemplateCapabilityCertification
            {
                ImageTemplateId = template.Id,
                ImageHash = template.ImageHash,
                Status = probe is { Success: false }
                    ? ImageTemplateCertificationStatus.Failed
                    : ImageTemplateCertificationStatus.Certified,
                CapabilitiesJson = JsonSerializer.Serialize((probe?.VerifiedCapabilities ?? capabilities)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray()),
                EvidenceDigest = evidenceDigest,
                ProbeKind = job.ProbeKind,
                ProbeStep = probe is null ? "evidence-verified" : "guest-supervisor-qga-disabled",
                WorkerNodeId = probe?.WorkerNodeId,
                ErrorCode = probe?.ErrorCode,
                ErrorDetail = Trim(probe?.ErrorDetail),
                DomainCreateDurationMs = probe?.DomainCreateDurationMs,
                GuestReadyDurationMs = probe?.GuestReadyDurationMs,
                FullProbeDurationMs = probe?.FullProbeDurationMs,
                PreparationContractVersion = probe is null
                    ? null
                    : GZCTF.GuestControl.Contracts.GuestControlProtocol.PreparationContractVersion,
                GuestProtocolVersion = probe is null
                    ? null
                    : GZCTF.GuestControl.Contracts.GuestControlProtocol.SchemaVersion,
                CertifiedById = job.ActorUserId
            };
            context.ImageTemplateCapabilityCertifications.Add(existing);
        }
        await context.SaveChangesAsync(cancellationToken);
        if (probe is { Success: false })
            throw new ApiOperationTerminalException(
                probe.ErrorCode ?? "image_certification_failed",
                probe.ErrorDetail ?? "Controlled VM certification failed.");
        if (probe is { Success: true })
        {
            template.VmRuntimeMode = VmRuntimeMode.Managed;
            template.VmArtifactStatus = VmArtifactStatus.Ready;
            template.PreparedArtifact!.Status = VmPreparedArtifactStatus.Ready;
            template.PreparedArtifact.EvidenceDigest = evidenceDigest;
            template.PreparedArtifact.PreparedAt ??= DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        if (!await operations.UpdateProgressAsync(
                operationId, leaseOwner, "certified", 1, 1,
                "image-template-certification", existing.Id.ToString(), null, cancellationToken))
            throw new OperationCanceledException("Image certification operation lease was lost.", cancellationToken);
    }

    static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : value.Length <= 1024 ? value : value[..1024];
}
