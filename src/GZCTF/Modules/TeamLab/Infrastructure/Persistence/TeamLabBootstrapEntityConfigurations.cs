using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabBootstrapExecutionEntityConfiguration : IEntityTypeConfiguration<TeamLabBootstrapExecution>
{
    public void Configure(EntityTypeBuilder<TeamLabBootstrapExecution> builder)
    {
        builder.ToTable("TeamLabBootstrapExecutions",
            table => table.HasCheckConstraint("CK_TeamLabBootstrapExecutions_Attempt", "\"Attempt\" = 1"));
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.Attempt).HasDefaultValue(1);
        builder.HasIndex(item => item.ExecutionId).IsUnique();
        builder.HasIndex(item => new
        {
            item.RuntimeId,
            item.Generation,
            item.AssetId,
            item.ProfileId,
            item.ProfileVersion,
            item.StepKey
        }).IsUnique();
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.BootstrapExecutions)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Asset)
            .WithMany()
            .HasForeignKey(item => item.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
