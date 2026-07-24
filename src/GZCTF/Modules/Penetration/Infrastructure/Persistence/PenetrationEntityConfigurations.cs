using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Penetration.Infrastructure.Persistence;

public sealed class PenetrationObjectiveEntityConfiguration : IEntityTypeConfiguration<PenetrationObjective>
{
    public void Configure(EntityTypeBuilder<PenetrationObjective> builder)
    {
        builder.ToTable("PenetrationObjectives");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.TopologyAssetKey).HasMaxLength(63);
        builder.Property(item => item.Key).HasMaxLength(63);
        builder.Property(item => item.Title).HasMaxLength(128);
        builder.Property(item => item.Description).HasMaxLength(1024);
        builder.Property(item => item.Category).HasMaxLength(64);
        builder.Property(item => item.StaticFlag).HasMaxLength(Limits.MaxFlagLength);
        builder.Property(item => item.FlagTemplate).HasMaxLength(Limits.MaxFlagTemplateLength);
        builder.Property(item => item.PrerequisiteObjectiveKeysJson).HasColumnType("jsonb");
        builder.HasIndex(item => new { item.GameId, item.Key }).IsUnique();
        builder.HasIndex(item => new { item.GameId, item.TopologyAssetKey });
        builder.HasOne<Game>()
            .WithMany()
            .HasForeignKey(item => item.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PenetrationGameLabBindingEntityConfiguration : IEntityTypeConfiguration<PenetrationGameLabBinding>
{
    public void Configure(EntityTypeBuilder<PenetrationGameLabBinding> builder)
    {
        builder.ToTable("PenetrationGameLabBindings");
        builder.HasKey(item => item.GameId);
        builder.HasIndex(item => item.TopologyId).IsUnique();
        builder.HasOne<Game>()
            .WithOne()
            .HasForeignKey<PenetrationGameLabBinding>(item => item.GameId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TeamLabTopology>()
            .WithMany()
            .HasForeignKey(item => item.TopologyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TeamLabTopologyRelease>()
            .WithMany()
            .HasForeignKey(item => item.ActiveReleaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PenetrationTeamRuntimeBindingEntityConfiguration : IEntityTypeConfiguration<PenetrationTeamRuntimeBinding>
{
    public void Configure(EntityTypeBuilder<PenetrationTeamRuntimeBinding> builder)
    {
        builder.ToTable("PenetrationTeamRuntimeBindings");
        builder.HasKey(item => new { item.GameId, item.TeamId });
        builder.HasIndex(item => item.RuntimeId).IsUnique();
        builder.HasIndex(item => item.DestroyOperationId)
            .IsUnique()
            .HasFilter("\"DestroyOperationId\" IS NOT NULL");
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasOne<Game>()
            .WithMany()
            .HasForeignKey(item => item.GameId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TeamLabRuntime>()
            .WithMany()
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PenetrationResetRecordEntityConfiguration : IEntityTypeConfiguration<PenetrationResetRecord>
{
    public void Configure(EntityTypeBuilder<PenetrationResetRecord> builder)
    {
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.FailureClass).HasConversion<byte>();
        builder.HasIndex(item => item.OperationId).IsUnique();
        builder.HasIndex(item => new { item.RuntimeId, item.TargetGeneration })
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)");
    }
}
