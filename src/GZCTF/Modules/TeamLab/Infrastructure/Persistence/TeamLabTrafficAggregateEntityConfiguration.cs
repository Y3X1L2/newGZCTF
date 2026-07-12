using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabTrafficFlowEntityConfiguration : IEntityTypeConfiguration<TeamLabTrafficFlow>
{
    public void Configure(EntityTypeBuilder<TeamLabTrafficFlow> builder)
    {
        builder.HasKey(item => new { item.CapturedAt, item.Id });
        builder.Property(item => item.Id).ValueGeneratedOnAdd();
        builder.Property(item => item.Fingerprint).HasColumnType("bytea").IsRequired();
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.CapturedAt, item.Id })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("IX_TeamLabFlows_Runtime_Generation_Time_Id");
        builder.HasIndex(item => new { item.ShardId, item.CapturedAt, item.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_TeamLabFlows_Shard_Time_Id");
        builder.HasIndex(item => new
            { item.CapturedAt, item.RuntimeId, item.Generation, item.Fingerprint })
            .IsUnique()
            .HasDatabaseName("UX_TeamLabFlows_Time_Runtime_Generation_Fingerprint");
        builder.HasOne(item => item.Runtime).WithMany(item => item.TrafficFlows)
            .HasForeignKey(item => item.RuntimeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Shard).WithMany().HasForeignKey(item => item.ShardId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.Network).WithMany().HasForeignKey(item => item.NetworkId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.WorkerNode).WithMany().HasForeignKey(item => item.WorkerNodeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TeamLabTrafficFlowAggregateEntityConfiguration : IEntityTypeConfiguration<TeamLabTrafficFlowAggregate>
{
    public void Configure(EntityTypeBuilder<TeamLabTrafficFlowAggregate> builder)
    {
        builder.ToTable("TeamLabTrafficFlowAggregates");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Protocol).HasMaxLength(16).IsRequired();
        builder.Property(item => item.SourcePrefix).HasMaxLength(64).IsRequired();
        builder.Property(item => item.DestinationPrefix).HasMaxLength(64).IsRequired();
        builder.HasIndex(item => new
            {
                item.BucketStart, item.RuntimeId, item.Generation, item.ShardId, item.NetworkId,
                item.Protocol, item.SourcePrefix, item.DestinationPrefix
            })
            .IsUnique()
            .HasDatabaseName("UX_TeamLabFlowAggregates_Dimensions");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.BucketStart })
            .HasDatabaseName("IX_TeamLabFlowAggregates_Runtime_Generation_Bucket");
    }
}
