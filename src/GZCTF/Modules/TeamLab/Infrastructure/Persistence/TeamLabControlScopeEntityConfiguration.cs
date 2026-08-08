using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabControlScopeEntityConfiguration : IEntityTypeConfiguration<TeamLabControlScope>
{
    public void Configure(EntityTypeBuilder<TeamLabControlScope> builder)
    {
        builder.ToTable("TeamLabControlScopes");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Key).HasMaxLength(96).IsRequired();
        builder.Property(item => item.DisplayName).HasMaxLength(128).IsRequired();
        builder.HasIndex(item => item.Key).IsUnique();
        builder.HasIndex(item => new { item.IsArchived, item.UpdatedAt });
    }
}
