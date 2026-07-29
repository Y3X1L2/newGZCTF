using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Exercise.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Exercise.Infrastructure.Persistence;

public sealed class ExerciseMutationJobEntityConfiguration : IEntityTypeConfiguration<ExerciseMutationJob>
{
    public void Configure(EntityTypeBuilder<ExerciseMutationJob> builder)
    {
        builder.ToTable("ExerciseMutationJobs");
        builder.HasKey(job => job.OperationId);
        builder.Property(job => job.PayloadJson).HasColumnType("jsonb");
        builder.Property(job => job.ResultJson).HasColumnType("jsonb");
        builder.HasIndex(job => job.ExerciseId);
        builder.HasIndex(job => new { job.Kind, job.CompletedAt });
        builder.HasOne<ApiOperation>()
            .WithOne()
            .HasForeignKey<ExerciseMutationJob>(job => job.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExerciseChallenge>()
            .WithMany()
            .HasForeignKey(job => job.ExerciseId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
