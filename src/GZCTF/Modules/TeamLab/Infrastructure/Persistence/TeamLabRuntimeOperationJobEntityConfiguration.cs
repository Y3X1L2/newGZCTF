using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabRuntimeOperationJobEntityConfiguration : IEntityTypeConfiguration<TeamLabRuntimeOperationJob>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntimeOperationJob> builder)
    {
        builder.ToTable("TeamLabRuntimeOperationJobs");
        builder.HasKey(item => item.OperationId);
        builder.Property(item => item.Kind).HasConversion<byte>();
        builder.Property(item => item.ProtectedPayload).HasColumnType("text");
        builder.Property(item => item.PayloadHash).HasMaxLength(128);
        builder.Property(item => item.ResultJson).HasColumnType("jsonb");
        builder.HasIndex(item => item.RuntimeId);
        builder.HasIndex(item => item.RuntimePublicId);
        builder.HasOne<ApiOperation>()
            .WithOne()
            .HasForeignKey<TeamLabRuntimeOperationJob>(item => item.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TeamLabRuntime>()
            .WithMany()
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
