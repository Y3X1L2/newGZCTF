using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabWebhookEntityConfigurations :
    IEntityTypeConfiguration<TeamLabWebhookSubscription>,
    IEntityTypeConfiguration<TeamLabWebhookDeliveryFailure>
{
    public void Configure(EntityTypeBuilder<TeamLabWebhookSubscription> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.ControlScopeId, item.Active });
        builder.HasIndex(item => item.ApiOperationId).IsUnique();
        builder.HasIndex(item => new { item.Active, item.NextDeliveryAt });
        builder.Property(item => item.EndpointUrl).HasMaxLength(2048);
        builder.Property(item => item.EventTypesJson).HasMaxLength(4096);
        builder.HasOne(item => item.ControlScope)
            .WithMany()
            .HasForeignKey(item => item.ControlScopeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Failures)
            .WithOne(item => item.Subscription)
            .HasForeignKey(item => item.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<TeamLabWebhookDeliveryFailure> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedOnAdd();
        builder.HasIndex(item => new { item.SubscriptionId, item.EventId });
    }
}
