using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Content.Infrastructure.Persistence;

public sealed class VmPreparedArtifactEntityConfiguration : IEntityTypeConfiguration<VmPreparedArtifact>
{
    public void Configure(EntityTypeBuilder<VmPreparedArtifact> builder)
    {
        builder.ToTable("VmPreparedArtifacts");
        builder.Property(item => item.OSType).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => item.ArtifactDigest);
        builder.HasOne(item => item.DerivedImageTemplate)
            .WithOne(item => item.PreparedArtifact)
            .HasForeignKey<ImageTemplate>(item => item.PreparedArtifactId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
