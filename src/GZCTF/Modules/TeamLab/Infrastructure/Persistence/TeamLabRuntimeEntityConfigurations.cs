using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabRuntimeFoundationEntityConfiguration : IEntityTypeConfiguration<TeamLabRuntime>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntime> builder)
    {
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => item.TopologyReleaseId);
        builder.HasIndex(item => new { item.CreatedById, item.ExternalReference })
            .IsUnique()
            .HasFilter("\"ExternalReference\" IS NOT NULL");
        builder.HasIndex(item => new { item.CreatedById, item.CreationIdempotencyKey })
            .IsUnique()
            .HasFilter("\"CreationIdempotencyKey\" IS NOT NULL");
        builder.Property(item => item.ExternalReference).HasMaxLength(256);
        builder.Property(item => item.CreationIdempotencyKey).HasMaxLength(128);
        builder.Property(item => item.CreateRequestHash).HasMaxLength(128);
        builder.HasOne<TeamLabTopologyRelease>()
            .WithMany()
            .HasForeignKey(item => item.TopologyReleaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserInfo>()
            .WithMany()
            .HasForeignKey(item => item.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TeamLabRuntimeShard>()
            .WithMany()
            .HasForeignKey(item => item.EntryShardId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TeamLabRuntimeNetworkFoundationEntityConfiguration : IEntityTypeConfiguration<TeamLabRuntimeNetwork>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntimeNetwork> builder)
    {
        builder.HasOne(item => item.NetworkLease)
            .WithOne()
            .HasForeignKey<TeamLabRuntimeNetwork>(item => item.NetworkLeaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabAccessGrantEntityConfiguration : IEntityTypeConfiguration<TeamLabAccessGrant>
{
    public void Configure(EntityTypeBuilder<TeamLabAccessGrant> builder)
    {
        builder.ToTable("TeamLabAccessGrants");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => item.ApiOperationId).IsUnique();
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.Revoked });
        builder.Property(item => item.Type).HasConversion<byte>();
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.AccessGrants)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApiOperation>()
            .WithMany()
            .HasForeignKey(item => item.ApiOperationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TeamLabRuntimeSecretEnvelopeEntityConfiguration : IEntityTypeConfiguration<TeamLabRuntimeSecretEnvelope>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntimeSecretEnvelope> builder)
    {
        builder.ToTable("TeamLabRuntimeSecretEnvelopes");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation }).IsUnique();
        builder.Property(item => item.ProtectedPayload).HasColumnType("text");
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.SecretEnvelopes)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabTrafficCaptureJobEntityConfiguration : IEntityTypeConfiguration<TeamLabTrafficCaptureJob>
{
    public void Configure(EntityTypeBuilder<TeamLabTrafficCaptureJob> builder)
    {
        builder.ToTable("TeamLabTrafficCaptureJobs");
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.RuntimeId, item.Status });
        builder.HasIndex(item => item.ApiOperationId)
            .IsUnique()
            .HasDatabaseName("UX_TeamLabCapture_ApiOperation");
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.TrafficCaptureJobs)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.ApiOperation)
            .WithMany()
            .HasForeignKey(item => item.ApiOperationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TeamLabTrafficCaptureSegmentEntityConfiguration
    : IEntityTypeConfiguration<TeamLabTrafficCaptureSegment>
{
    public void Configure(EntityTypeBuilder<TeamLabTrafficCaptureSegment> builder)
    {
        builder.ToTable("TeamLabTrafficCaptureSegments");
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.CaptureJobId, item.ObservationPointId }).IsUnique();
        builder.HasIndex(item => new { item.Status, item.UpdatedAt });
        builder.HasOne(item => item.CaptureJob)
            .WithMany(item => item.Segments)
            .HasForeignKey(item => item.CaptureJobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode)
            .WithMany()
            .HasForeignKey(item => item.WorkerNodeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.ObservationPoint)
            .WithMany()
            .HasForeignKey(item => item.ObservationPointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
