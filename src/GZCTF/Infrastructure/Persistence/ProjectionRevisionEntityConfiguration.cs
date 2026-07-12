using GZCTF.Infrastructure.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Infrastructure.Persistence;

public sealed class ProjectionRevisionEntityConfiguration : IEntityTypeConfiguration<ProjectionRevision>
{
    public void Configure(EntityTypeBuilder<ProjectionRevision> builder)
    {
        builder.ToTable("ProjectionRevisions");
        builder.HasKey(item => new { item.Projection, item.ResourceKey });
        builder.Property(item => item.Projection).HasMaxLength(64);
        builder.Property(item => item.ResourceKey).HasMaxLength(160);
        builder.Property(item => item.Version).IsConcurrencyToken();
        builder.HasIndex(item => item.UpdatedAt);
    }
}
