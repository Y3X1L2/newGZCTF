using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Modules.Audit.Infrastructure.Persistence;

public sealed class ExternalApiRequestAuditEntityConfiguration
    : IEntityTypeConfiguration<ExternalApiRequestAudit>
{
    public void Configure(EntityTypeBuilder<ExternalApiRequestAudit> builder)
    {
        builder.ToTable("ExternalApiRequestAudits");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.TraceId).HasMaxLength(128).IsRequired();
        builder.Property(audit => audit.Scopes).HasMaxLength(1024).IsRequired();
        builder.Property(audit => audit.Method).HasMaxLength(16).IsRequired();
        builder.Property(audit => audit.RouteKey).HasMaxLength(512).IsRequired();
        builder.Property(audit => audit.ResourceType).HasMaxLength(64);
        builder.Property(audit => audit.ResourceId).HasMaxLength(128);
        builder.Property(audit => audit.ErrorCode).HasMaxLength(128);
        builder.Property(audit => audit.RemoteIp).HasMaxLength(64);
        builder.HasIndex(audit => audit.CreatedAt);
        builder.HasIndex(audit => audit.TraceId);
        builder.HasIndex(audit => audit.ApiTokenId);
        builder.HasIndex(audit => audit.OperationId);

        builder.HasOne<ApiTokenEntity>()
            .WithMany()
            .HasForeignKey(audit => audit.ApiTokenId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<UserInfo>()
            .WithMany()
            .HasForeignKey(audit => audit.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApiOperation>()
            .WithMany()
            .HasForeignKey(audit => audit.OperationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
