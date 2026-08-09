using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class ImageImportOperationHandler(
    AppDbContext context,
    ImageImportApplicationService imports,
    ApiOperationService operations,
    ImageDistributionService distribution,
    IImageImportStagingStore staging) : IApiOperationHandler
{
    public string Kind => ImageImportApplicationService.OperationKind;

    public async Task OnTerminalFailureAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var stagedPath = await context.ImageImportJobs.AsNoTracking()
            .Where(job => job.OperationId == operationId &&
                          job.SourceKind != ImageImportSourceKind.DockerReference)
            .Select(job => job.StagedPath)
            .SingleOrDefaultAsync(cancellationToken);
        await staging.DeleteAsync(stagedPath, cancellationToken);
    }

    public async Task ExecuteAsync(
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var job = await context.ImageImportJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken)
            ?? throw new ImageImportNotFoundException();
        ImageTemplate template;
        try
        {
            if (job.ImageTemplateId.HasValue)
            {
                template = await context.ImageTemplates.SingleOrDefaultAsync(
                    item => item.Id == job.ImageTemplateId.Value, cancellationToken)
                    ?? throw new ApiOperationTerminalException(
                        "image_template_missing", "The imported image template no longer exists.");
            }
            else
            {
                await RequireLeaseAsync(
                    operationId, leaseOwner, "image-importing", 0, 3, cancellationToken);
                var imported = await imports.ExecuteJobAsync(job, true, cancellationToken);
                template = await context.ImageTemplates.SingleAsync(
                    item => item.Id == imported.Id, cancellationToken);
                await RequireLeaseAsync(
                    operationId,
                    leaseOwner,
                    "image-ready",
                    1,
                    3,
                    cancellationToken,
                    template.Id);
            }
        }
        catch (ApiOperationTerminalException)
        {
            if (job.SourceKind != ImageImportSourceKind.DockerReference)
                await staging.DeleteAsync(job.StagedPath, CancellationToken.None);
            throw;
        }

        if (job.SourceKind != ImageImportSourceKind.DockerReference)
            await staging.DeleteAsync(job.StagedPath, CancellationToken.None);

        await RequireLeaseAsync(
            operationId,
            leaseOwner,
            "image-distributing",
            2,
            3,
            cancellationToken,
            template.Id);
        var distributionRecords = await distribution.DistributeToCapableNodesAsync(
            template, cancellationToken);
        await RequireLeaseAsync(
            operationId,
            leaseOwner,
            "image-distribution-queued",
            3,
            3,
            cancellationToken,
            template.Id);
    }

    private async Task RequireLeaseAsync(
        Guid operationId,
        string leaseOwner,
        string stage,
        long current,
        long total,
        CancellationToken cancellationToken,
        int? templateId = null)
    {
        var updated = await operations.UpdateProgressAsync(
            operationId,
            leaseOwner,
            stage,
            current,
            total,
            templateId.HasValue ? "image-template" : null,
            templateId?.ToString(),
            null,
            cancellationToken);
        if (!updated)
            throw new OperationCanceledException("The operation execution lease was lost.", cancellationToken);
    }
}
