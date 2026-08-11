using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Provisioning.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Provisioning.Infrastructure.Persistence;

public sealed class AcademicImportJobEntityConfiguration : IEntityTypeConfiguration<AcademicImportJob>
{
    public void Configure(EntityTypeBuilder<AcademicImportJob> builder)
    {
        builder.ToTable("AcademicImportJobs");
        builder.HasKey(job => job.OperationId);
        builder.Property(job => job.PayloadJson).HasColumnType("jsonb");
        builder.Property(job => job.ResultJson).HasColumnType("jsonb");
        builder.HasIndex(job => new { job.Kind, job.CompletedAt });
        builder.HasOne<ApiOperation>()
            .WithOne()
            .HasForeignKey<AcademicImportJob>(job => job.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
