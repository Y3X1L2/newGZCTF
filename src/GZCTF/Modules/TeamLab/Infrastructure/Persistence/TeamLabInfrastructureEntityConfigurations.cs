using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabRuntimeInfrastructureEntityConfiguration
    : IEntityTypeConfiguration<TeamLabRuntimeInfrastructure>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntimeInfrastructure> builder)
    {
        builder.ToTable("TeamLabRuntimeInfrastructure");
        builder.Property(item => item.Kind).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.InterfaceSummaryJson).HasColumnType("jsonb");
        builder.Property(item => item.ConnectionSummaryJson).HasColumnType("jsonb");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.TopologyKey }).IsUnique();
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.Infrastructure)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabRuntimeInfrastructureFragmentEntityConfiguration
    : IEntityTypeConfiguration<TeamLabRuntimeInfrastructureFragment>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntimeInfrastructureFragment> builder)
    {
        builder.ToTable("TeamLabRuntimeInfrastructureFragments");
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.InterfaceSummaryJson).HasColumnType("jsonb");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.InfrastructureId, item.ShardId }).IsUnique();
        builder.HasOne(item => item.Infrastructure)
            .WithMany(item => item.Fragments)
            .HasForeignKey(item => item.InfrastructureId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Shard)
            .WithMany()
            .HasForeignKey(item => item.ShardId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode)
            .WithMany()
            .HasForeignKey(item => item.WorkerNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabExecutionPlanSnapshotEntityConfiguration
    : IEntityTypeConfiguration<TeamLabExecutionPlanSnapshot>
{
    public void Configure(EntityTypeBuilder<TeamLabExecutionPlanSnapshot> builder)
    {
        builder.ToTable("TeamLabExecutionPlanSnapshots");
        builder.Property(item => item.PlanDigest).HasMaxLength(96);
        builder.Property(item => item.PlanJson).HasColumnType("jsonb");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.ShardId }).IsUnique();
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.ExecutionPlanSnapshots)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Shard)
            .WithMany()
            .HasForeignKey(item => item.ShardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabFabricLinkLeaseEntityConfiguration : IEntityTypeConfiguration<TeamLabFabricLinkLease>
{
    public void Configure(EntityTypeBuilder<TeamLabFabricLinkLease> builder)
    {
        builder.ToTable("TeamLabFabricLinkLeases");
        builder.Property(item => item.AllocatedCidr).HasColumnType("cidr");
        builder.HasIndex(item => item.ReleasedAt);
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.ShardId }).IsUnique();
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.FabricLinkLeases)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Shard)
            .WithMany()
            .HasForeignKey(item => item.ShardId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode)
            .WithMany()
            .HasForeignKey(item => item.WorkerNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabRuntimeDependencyStateEntityConfiguration
    : IEntityTypeConfiguration<TeamLabRuntimeDependencyState>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntimeDependencyState> builder)
    {
        builder.ToTable("TeamLabRuntimeDependencyStates");
        builder.Property(item => item.Condition).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasIndex(item => new
        {
            item.RuntimeId,
            item.Generation,
            item.AssetKey,
            item.DependsOnKey,
            item.Condition
        }).IsUnique();
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.DependencyStates)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
