using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Content.Infrastructure.Persistence;

public sealed class ImageImportJobEntityConfiguration : IEntityTypeConfiguration<ImageImportJob>
{
    public void Configure(EntityTypeBuilder<ImageImportJob> builder)
    {
        builder.ToTable("ImageImportJobs");
        builder.HasKey(job => job.OperationId);
        builder.Property(job => job.SourceReference).HasMaxLength(512);
        builder.Property(job => job.StagedPath).HasMaxLength(512);
        builder.Property(job => job.OriginalFileName).HasMaxLength(256);
        builder.Property(job => job.ExpectedDigest).HasMaxLength(128);
        builder.Property(job => job.RequestedName).HasMaxLength(256);
        builder.Property(job => job.RequestedVmNetworkMode).HasConversion<byte>();
        builder.HasIndex(job => job.ImageTemplateId);
        builder.HasIndex(job => new { job.CreatedById, job.RequestedName });

        builder.HasOne<ApiOperation>()
            .WithOne()
            .HasForeignKey<ImageImportJob>(job => job.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<UserInfo>()
            .WithMany()
            .HasForeignKey(job => job.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ImageTemplate>()
            .WithMany()
            .HasForeignKey(job => job.ImageTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
