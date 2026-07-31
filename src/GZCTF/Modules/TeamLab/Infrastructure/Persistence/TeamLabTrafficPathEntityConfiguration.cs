using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabTrafficPathEntityConfiguration : IEntityTypeConfiguration<TeamLabTrafficPath>
{
    public void Configure(EntityTypeBuilder<TeamLabTrafficPath> builder)
    {
        builder.ToTable("TeamLabTrafficPaths");
        builder.Property(item => item.Confidence).HasConversion<byte>();
        builder.Property(item => item.EvidenceFingerprint).HasColumnType("bytea").IsRequired();
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.EvidenceFingerprint }).IsUnique();
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.StartedAt, item.Id })
            .HasDatabaseName("IX_TeamLabPaths_Runtime_Generation_Time_Id");
        builder.HasOne(item => item.Runtime).WithMany(item => item.TrafficPaths)
            .HasForeignKey(item => item.RuntimeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabTrafficPathHopEntityConfiguration : IEntityTypeConfiguration<TeamLabTrafficPathHop>
{
    public void Configure(EntityTypeBuilder<TeamLabTrafficPathHop> builder)
    {
        builder.ToTable("TeamLabTrafficPathHops");
        builder.Property(item => item.EvidenceKind).HasConversion<byte>();
        builder.HasIndex(item => new { item.PathId, item.Ordinal }).IsUnique();
        builder.HasOne(item => item.Path).WithMany(item => item.Hops)
            .HasForeignKey(item => item.PathId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Observation).WithMany()
            .HasForeignKey(item => item.ObservationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.ObservationPoint).WithMany()
            .HasForeignKey(item => item.ObservationPointId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabTrafficCorrelationCursorEntityConfiguration
    : IEntityTypeConfiguration<TeamLabTrafficCorrelationCursor>
{
    public void Configure(EntityTypeBuilder<TeamLabTrafficCorrelationCursor> builder)
    {
        builder.ToTable("TeamLabTrafficCorrelationCursors");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation }).IsUnique();
        builder.HasOne(item => item.Runtime).WithMany(item => item.TrafficCorrelationCursors)
            .HasForeignKey(item => item.RuntimeId).OnDelete(DeleteBehavior.Cascade);
    }
}
