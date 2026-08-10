using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabRolloutEntityConfiguration : IEntityTypeConfiguration<TeamLabRollout>
{
    public void Configure(EntityTypeBuilder<TeamLabRollout> builder)
    {
        builder.ToTable("TeamLabRollouts");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => item.ControlScopeId);
        builder.HasIndex(item => new { item.ControlScopeId, item.AdapterKind, item.ExternalReference, item.ReleaseId })
            .IsUnique()
            .HasFilter("\"Status\" NOT IN (5, 8)");
        builder.HasIndex(item => new { item.Status, item.UpdatedAt });
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.Revision).IsConcurrencyToken();
        builder.HasOne(item => item.Release).WithMany().HasForeignKey(item => item.ReleaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.ControlScope).WithMany().HasForeignKey(item => item.ControlScopeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabRolloutTargetEntityConfiguration : IEntityTypeConfiguration<TeamLabRolloutTarget>
{
    public void Configure(EntityTypeBuilder<TeamLabRolloutTarget> builder)
    {
        builder.ToTable("TeamLabRolloutTargets");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.RolloutId, item.ExternalSubject }).IsUnique();
        builder.HasIndex(item => new { item.RolloutId, item.Status, item.Id });
        builder.HasIndex(item => item.RuntimeId).IsUnique().HasFilter("\"RuntimeId\" IS NOT NULL");
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.IsDesired).IsRequired();
        builder.Property(item => item.RebuildRequested).IsRequired();
        builder.HasOne(item => item.Rollout).WithMany(item => item.Targets).HasForeignKey(item => item.RolloutId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Runtime).WithMany().HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
