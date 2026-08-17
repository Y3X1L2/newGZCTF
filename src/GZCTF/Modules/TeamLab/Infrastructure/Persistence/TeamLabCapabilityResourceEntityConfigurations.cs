using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabDevicePackageEntityConfiguration : IEntityTypeConfiguration<TeamLabDevicePackage>
{
    public void Configure(EntityTypeBuilder<TeamLabDevicePackage> builder)
    {
        builder.ToTable("TeamLabDevicePackages");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.Name, item.Version }).IsUnique();
        builder.HasIndex(item => new { item.IsEnabled, item.Name });
        builder.Property(item => item.ArtifactKind).HasConversion<byte>();
    }
}

public sealed class TeamLabConnectorEntityConfiguration : IEntityTypeConfiguration<TeamLabConnector>
{
    public void Configure(EntityTypeBuilder<TeamLabConnector> builder)
    {
        builder.ToTable("TeamLabConnectors");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => item.Name).IsUnique();
        builder.HasIndex(item => item.ControlScopeId);
        builder.Property(item => item.Kind).HasConversion<byte>();
        builder.Property(item => item.Health).HasConversion<byte>();
        builder.HasOne(item => item.ControlScope).WithMany()
            .HasForeignKey(item => item.ControlScopeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabConnectorLeaseEntityConfiguration : IEntityTypeConfiguration<TeamLabConnectorLease>
{
    public void Configure(EntityTypeBuilder<TeamLabConnectorLease> builder)
    {
        builder.ToTable("TeamLabConnectorLeases");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.ConnectorId, item.RuntimeId }).IsUnique()
            .HasFilter("\"ReleasedAt\" IS NULL");
        builder.HasIndex(item => new { item.ConnectorId, item.Slot }).IsUnique()
            .HasFilter("\"ReleasedAt\" IS NULL");
        builder.HasIndex(item => item.RuntimeId);
        builder.Property(item => item.ReleaseReason).HasConversion<byte>();
        builder.HasOne(item => item.Connector).WithMany(item => item.Leases)
            .HasForeignKey(item => item.ConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Runtime).WithMany()
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabLinkPolicyEntityConfiguration : IEntityTypeConfiguration<TeamLabLinkPolicy>
{
    public void Configure(EntityTypeBuilder<TeamLabLinkPolicy> builder)
    {
        builder.ToTable("TeamLabLinkPolicies");
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.RuntimeId, item.NetworkKey, item.AssetKey, item.Kind }).IsUnique()
            .HasFilter("\"Status\" = 1");
        builder.HasIndex(item => new { item.Status, item.RecoverAt });
        builder.Property(item => item.Kind).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.RecoverOrigin).HasConversion<byte>();
        builder.Property(item => item.Revision).IsConcurrencyToken();
        builder.HasOne(item => item.Runtime).WithMany()
            .HasForeignKey(item => item.RuntimeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
