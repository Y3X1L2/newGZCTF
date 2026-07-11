using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
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
        builder.Property(item => item.ExternalReference).HasMaxLength(256);
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
        builder.HasOne<TeamLabNetworkLease>()
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
        builder.Property(item => item.Type).HasConversion<byte>();
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.AccessGrants)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabRuntimeSecretEnvelopeEntityConfiguration : IEntityTypeConfiguration<TeamLabRuntimeSecretEnvelope>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntimeSecretEnvelope> builder)
    {
        builder.ToTable("TeamLabRuntimeSecretEnvelopes");
        builder.Property(item => item.ProtectedPayload).HasColumnType("text");
        builder.HasOne(item => item.Runtime)
            .WithMany(item => item.SecretEnvelopes)
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
