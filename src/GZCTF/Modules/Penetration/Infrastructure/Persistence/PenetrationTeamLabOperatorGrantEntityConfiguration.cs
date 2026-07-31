using GZCTF.Modules.Penetration.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Penetration.Infrastructure.Persistence;

public sealed class PenetrationTeamLabOperatorGrantEntityConfiguration : IEntityTypeConfiguration<PenetrationTeamLabOperatorGrant>
{
    public void Configure(EntityTypeBuilder<PenetrationTeamLabOperatorGrant> builder)
    {
        builder.ToTable("PenetrationTeamLabOperatorGrants");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Permissions).HasConversion<byte>();
        builder.HasIndex(item => new { item.GameId, item.UserId }).IsUnique();
        builder.HasOne(item => item.Game).WithMany().HasForeignKey(item => item.GameId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.GrantedBy).WithMany().HasForeignKey(item => item.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
