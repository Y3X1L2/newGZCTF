using GZCTF.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Modules.Identity.Infrastructure.Persistence;

public sealed class ApiTokenEntityConfiguration : IEntityTypeConfiguration<ApiTokenEntity>
{
    public void Configure(EntityTypeBuilder<ApiTokenEntity> builder)
    {
        builder.ToTable("ApiTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Name).HasMaxLength(128).IsRequired();
        builder.Property(token => token.SecretHash).HasMaxLength(32).IsRequired();
        builder.Property(token => token.CreatorId).IsRequired();
        builder.HasIndex(token => token.CreatorId);
        builder.HasOne<UserInfo>()
            .WithMany()
            .HasForeignKey(token => token.CreatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(token => token.Scopes)
            .WithOne()
            .HasForeignKey(scope => scope.TokenId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(token => token.Resources)
            .WithOne()
            .HasForeignKey(resource => resource.TokenId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ApiTokenScopeGrantEntityConfiguration : IEntityTypeConfiguration<ApiTokenScopeGrant>
{
    public void Configure(EntityTypeBuilder<ApiTokenScopeGrant> builder)
    {
        builder.ToTable("ApiTokenScopeGrants");
        builder.HasKey(scope => new { scope.TokenId, scope.Scope });
        builder.Property(scope => scope.Scope).HasMaxLength(128);
    }
}

public sealed class ApiTokenResourceGrantEntityConfiguration : IEntityTypeConfiguration<ApiTokenResourceGrant>
{
    public void Configure(EntityTypeBuilder<ApiTokenResourceGrant> builder)
    {
        builder.ToTable("ApiTokenResourceGrants");
        builder.HasKey(resource => new { resource.TokenId, resource.ResourceType, resource.ResourceId });
        builder.Property(resource => resource.ResourceType).HasMaxLength(64);
        builder.Property(resource => resource.ResourceId).HasMaxLength(128);
    }
}
