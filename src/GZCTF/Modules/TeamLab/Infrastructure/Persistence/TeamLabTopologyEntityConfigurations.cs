using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabTopologyEntityConfiguration : IEntityTypeConfiguration<TeamLabTopology>
{
    public void Configure(EntityTypeBuilder<TeamLabTopology> builder)
    {
        builder.ToTable("TeamLabTopologies");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(128);
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => item.OwnerUserId);
        builder.HasOne<UserInfo>()
            .WithMany()
            .HasForeignKey(item => item.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TeamLabTopologyNetworkEntityConfiguration : IEntityTypeConfiguration<TeamLabTopologyNetwork>
{
    public void Configure(EntityTypeBuilder<TeamLabTopologyNetwork> builder)
    {
        builder.ToTable("TeamLabTopologyNetworks");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Key).HasMaxLength(63);
        builder.Property(item => item.Name).HasMaxLength(128);
        builder.Property(item => item.AddressPoolCidr).HasMaxLength(64);
        builder.HasIndex(item => new { item.TopologyId, item.Key }).IsUnique();
        builder.HasOne(item => item.Topology)
            .WithMany(item => item.Networks)
            .HasForeignKey(item => item.TopologyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabTopologyAssetEntityConfiguration : IEntityTypeConfiguration<TeamLabTopologyAsset>
{
    public void Configure(EntityTypeBuilder<TeamLabTopologyAsset> builder)
    {
        builder.ToTable("TeamLabTopologyAssets");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Key).HasMaxLength(63);
        builder.Property(item => item.Name).HasMaxLength(128);
        builder.Property(item => item.Kind).HasConversion<byte>();
        builder.Property(item => item.EnvironmentJson).HasColumnType("jsonb");
        builder.Property(item => item.StartCommand).HasMaxLength(512);
        builder.Property(item => item.HealthCheckKind).HasConversion<byte?>();
        builder.HasIndex(item => new { item.TopologyId, item.Key }).IsUnique();
        builder.HasIndex(item => item.ImageTemplateId);
        builder.HasOne(item => item.Topology)
            .WithMany(item => item.Assets)
            .HasForeignKey(item => item.TopologyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ImageTemplate>()
            .WithMany()
            .HasForeignKey(item => item.ImageTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabTopologyInterfaceEntityConfiguration : IEntityTypeConfiguration<TeamLabTopologyInterface>
{
    public void Configure(EntityTypeBuilder<TeamLabTopologyInterface> builder)
    {
        builder.ToTable("TeamLabTopologyInterfaces");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Key).HasMaxLength(63);
        builder.HasIndex(item => new { item.AssetId, item.Key }).IsUnique();
        builder.HasIndex(item => new { item.NetworkId, item.AssetId });
        builder.HasOne(item => item.Asset)
            .WithMany(item => item.Interfaces)
            .HasForeignKey(item => item.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Network)
            .WithMany(item => item.Interfaces)
            .HasForeignKey(item => item.NetworkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabTopologyConnectionEntityConfiguration : IEntityTypeConfiguration<TeamLabTopologyConnection>
{
    public void Configure(EntityTypeBuilder<TeamLabTopologyConnection> builder)
    {
        builder.ToTable("TeamLabTopologyConnections");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Key).HasMaxLength(63);
        builder.Property(item => item.FromNetworkKey).HasMaxLength(63);
        builder.Property(item => item.ToNetworkKey).HasMaxLength(63);
        builder.Property(item => item.ViaAssetKey).HasMaxLength(63);
        builder.HasIndex(item => new { item.TopologyId, item.Key }).IsUnique();
        builder.HasOne(item => item.Topology)
            .WithMany(item => item.Connections)
            .HasForeignKey(item => item.TopologyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabTopologyReleaseEntityConfiguration : IEntityTypeConfiguration<TeamLabTopologyRelease>
{
    public void Configure(EntityTypeBuilder<TeamLabTopologyRelease> builder)
    {
        builder.ToTable("TeamLabTopologyReleases");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.CanonicalJson).HasColumnType("jsonb");
        builder.Property(item => item.ContentHash).HasMaxLength(128);
        builder.HasIndex(item => new { item.TopologyId, item.Version }).IsUnique();
        builder.HasIndex(item => new { item.TopologyId, item.SourceRevision, item.ContentHash }).IsUnique();
        builder.HasOne(item => item.Topology)
            .WithMany(item => item.Releases)
            .HasForeignKey(item => item.TopologyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<UserInfo>()
            .WithMany()
            .HasForeignKey(item => item.PublishedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TeamLabNetworkLeaseEntityConfiguration : IEntityTypeConfiguration<TeamLabNetworkLease>
{
    public void Configure(EntityTypeBuilder<TeamLabNetworkLease> builder)
    {
        builder.ToTable("TeamLabNetworkLeases");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.AllocatedCidr).HasColumnType("cidr");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.TopologyNetworkId }).IsUnique();
        builder.HasIndex(item => item.ReleasedAt);
        builder.HasOne<GZCTF.Models.Data.TeamLabRuntime>()
            .WithMany()
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.TopologyNetwork)
            .WithMany()
            .HasForeignKey(item => item.TopologyNetworkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
