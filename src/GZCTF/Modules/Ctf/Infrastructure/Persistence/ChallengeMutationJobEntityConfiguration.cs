using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Ctf.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Ctf.Infrastructure.Persistence;

public sealed class ChallengeMutationJobEntityConfiguration : IEntityTypeConfiguration<ChallengeMutationJob>
{
    public void Configure(EntityTypeBuilder<ChallengeMutationJob> builder)
    {
        builder.ToTable("ChallengeMutationJobs");
        builder.HasKey(job => job.OperationId);
        builder.Property(job => job.PayloadJson).HasColumnType("jsonb");
        builder.Property(job => job.ResultJson).HasColumnType("jsonb");
        builder.HasIndex(job => job.GameId);
        builder.HasIndex(job => new { job.Kind, job.CompletedAt });
        builder.HasOne<ApiOperation>()
            .WithOne()
            .HasForeignKey<ChallengeMutationJob>(job => job.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Game>()
            .WithMany()
            .HasForeignKey(job => job.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
