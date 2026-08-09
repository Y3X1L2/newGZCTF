using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public sealed class ImageTemplateCertificationTests
{
    [Fact]
    public async Task Submission_IsIdempotentAndRejectsOsCapabilityMismatch()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 9,
            Name = "ubuntu",
            OSType = OSType.Linux,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Ready,
            ImageHash = new string('a', 64)
        });
        await context.SaveChangesAsync();
        var service = new ImageTemplateCertificationService(context, new ExternalApiAuditContext());
        var tokenId = Guid.NewGuid();
        var actor = new ActorContext(Guid.NewGuid(), Role.Teacher, tokenId);
        var request = new ImageTemplateCertificationRequest(
            [ImageTemplateCapabilityIds.GuestQga], new string('b', 64), "external-evidence");

        var first = await service.SubmitAsync(
            tokenId, actor, 9, "cert-1", request, CancellationToken.None);
        var repeated = await service.SubmitAsync(
            tokenId, actor, 9, "cert-1", request, CancellationToken.None);

        Assert.Equal(first.Operation.Id, repeated.Operation.Id);
        Assert.True(repeated.Reused);
        await Assert.ThrowsAsync<ImageTemplateCertificationContractException>(() => service.SubmitAsync(
            tokenId,
            actor,
            9,
            "cert-2",
            new ImageTemplateCertificationRequest(
                [ImageTemplateCapabilityIds.WindowsPowerShell], new string('c', 64), "external-evidence"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ControlledProbe_GeneratesEvidenceAndRejectsCallerSuppliedDigest()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 10,
            Name = "windows",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Ready,
            ImageHash = new string('d', 64)
        });
        await context.SaveChangesAsync();
        var service = new ImageTemplateCertificationService(context, new ExternalApiAuditContext());
        var tokenId = Guid.NewGuid();
        var actor = new ActorContext(Guid.NewGuid(), Role.Teacher, tokenId);

        var accepted = await service.SubmitAsync(
            tokenId,
            actor,
            10,
            "controlled-cert-1",
            new ImageTemplateCertificationRequest(
                [ImageTemplateCapabilityIds.GuestQga, ImageTemplateCapabilityIds.WindowsPowerShell]),
            CancellationToken.None);

        var job = await context.ImageTemplateCertificationJobs.SingleAsync();
        Assert.Equal("controlled-probe", job.ProbeKind);
        Assert.Null(job.EvidenceDigest);
        Assert.Equal(accepted.Operation.Id, job.OperationId);
        await Assert.ThrowsAsync<ImageTemplateCertificationContractException>(() => service.SubmitAsync(
            tokenId,
            actor,
            10,
            "controlled-cert-2",
            new ImageTemplateCertificationRequest(
                [ImageTemplateCapabilityIds.GuestQga], new string('e', 64)),
            CancellationToken.None));
    }
}
