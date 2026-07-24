using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabObservationPointEntityConfiguration : IEntityTypeConfiguration<TeamLabObservationPoint>
{
    public void Configure(EntityTypeBuilder<TeamLabObservationPoint> builder)
    {
        builder.ToTable("TeamLabObservationPoints");
        builder.Property(item => item.Kind).HasConversion<byte>();
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new
        {
            item.RuntimeId,
            item.Generation,
            item.WorkerNodeId,
            item.InterfaceToken,
            item.Kind
        }).IsUnique();
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.ObservationPoints)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Shard)
            .WithMany()
            .HasForeignKey(item => item.ShardId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Network)
            .WithMany()
            .HasForeignKey(item => item.NetworkId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.InfrastructureFragment)
            .WithMany()
            .HasForeignKey(item => item.InfrastructureFragmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Asset)
            .WithMany()
            .HasForeignKey(item => item.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode)
            .WithMany()
            .HasForeignKey(item => item.WorkerNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
