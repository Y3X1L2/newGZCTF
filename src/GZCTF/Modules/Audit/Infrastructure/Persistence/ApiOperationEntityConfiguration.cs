using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Modules.Audit.Infrastructure.Persistence;

public sealed class ApiOperationEntityConfiguration : IEntityTypeConfiguration<ApiOperation>
{
    public void Configure(EntityTypeBuilder<ApiOperation> builder)
    {
        builder.ToTable("ApiOperations");
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Kind).HasMaxLength(128).IsRequired();
        builder.Property(operation => operation.Stage).HasMaxLength(128).IsRequired();
        builder.Property(operation => operation.RouteKey).HasMaxLength(256).IsRequired();
        builder.Property(operation => operation.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(operation => operation.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(operation => operation.ResourceType).HasMaxLength(64);
        builder.Property(operation => operation.ResourceId).HasMaxLength(128);
        builder.Property(operation => operation.LeaseOwner).HasMaxLength(128);
        builder.Property(operation => operation.ErrorCode).HasMaxLength(128);
        builder.Property(operation => operation.ErrorDetail).HasMaxLength(2048);

        builder.HasIndex(operation => new
            { operation.ApiTokenId, operation.RouteKey, operation.IdempotencyKey })
            .IsUnique();
        builder.HasIndex(operation => new { operation.Status, operation.NextAttemptAt })
            .HasFilter("\"Status\" = 0");
        builder.HasIndex(operation => new { operation.Status, operation.LeaseExpiresAt })
            .HasFilter("\"Status\" = 1");
        builder.HasIndex(operation => operation.ActorUserId);
        builder.HasIndex(operation => operation.CreatedAt);

        builder.HasOne<ApiTokenEntity>()
            .WithMany()
            .HasForeignKey(operation => operation.ApiTokenId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserInfo>()
            .WithMany()
            .HasForeignKey(operation => operation.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<DeploymentQueueTicket>()
            .WithMany()
            .HasForeignKey(operation => operation.DeploymentQueueTicketId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
