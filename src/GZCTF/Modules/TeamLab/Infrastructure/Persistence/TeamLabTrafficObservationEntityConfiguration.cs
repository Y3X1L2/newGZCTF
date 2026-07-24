using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabTrafficObservationEntityConfiguration : IEntityTypeConfiguration<TeamLabTrafficObservation>
{
    public void Configure(EntityTypeBuilder<TeamLabTrafficObservation> builder)
    {
        builder.ToTable("TeamLabTrafficObservations");
        builder.Property(item => item.EvidenceKind).HasConversion<byte>();
        builder.Property(item => item.PacketFingerprint).HasColumnType("bytea");
        builder.Property(item => item.FlowFingerprint).HasColumnType("bytea").IsRequired();
        builder.Property(item => item.ProcessIdentityHash).HasColumnType("bytea");
        builder.HasIndex(item => new
        {
            item.RuntimeId,
            item.Generation,
            item.ObservationPointId,
            item.SourceSequence
        }).IsUnique();
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.ObservedAt, item.Id })
            .HasDatabaseName("IX_TeamLabObservations_Runtime_Generation_Time_Id");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.PacketFingerprint, item.ObservedAt })
            .HasFilter("\"PacketFingerprint\" IS NOT NULL")
            .HasDatabaseName("IX_TeamLabObservations_PacketFingerprint");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.ProcessIdentityHash, item.ObservedAt })
            .HasFilter("\"ProcessIdentityHash\" IS NOT NULL")
            .HasDatabaseName("IX_TeamLabObservations_ProcessIdentity");
        builder.HasOne(item => item.Runtime).WithMany(item => item.TrafficObservations)
            .HasForeignKey(item => item.RuntimeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.ObservationPoint).WithMany()
            .HasForeignKey(item => item.ObservationPointId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode).WithMany()
            .HasForeignKey(item => item.WorkerNodeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabObservationCursorEntityConfiguration : IEntityTypeConfiguration<TeamLabObservationCursor>
{
    public void Configure(EntityTypeBuilder<TeamLabObservationCursor> builder)
    {
        builder.ToTable("TeamLabObservationCursors");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.WorkerNodeId }).IsUnique();
        builder.HasOne(item => item.Runtime).WithMany(item => item.ObservationCursors)
            .HasForeignKey(item => item.RuntimeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode).WithMany()
            .HasForeignKey(item => item.WorkerNodeId).OnDelete(DeleteBehavior.Restrict);
    }
}
