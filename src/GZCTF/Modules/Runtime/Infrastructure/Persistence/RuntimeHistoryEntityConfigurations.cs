using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Runtime.Infrastructure.Persistence;

public sealed class DeploymentQueueTicketEntityConfiguration : IEntityTypeConfiguration<DeploymentQueueTicket>
{
    public void Configure(EntityTypeBuilder<DeploymentQueueTicket> builder)
    {
        builder.Property(item => item.Kind).HasConversion<byte>();
        builder.Property(item => item.Operation).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.Stage).HasConversion<byte>();
        builder.Property(item => item.ErrorCategory).HasConversion<byte>();
        builder.HasIndex(item => item.ActiveIdentity).IsUnique()
            .HasFilter("\"Status\" IN (0, 1, 2, 3)")
            .HasDatabaseName("UX_DeploymentQueueTickets_ActiveIdentity");
        builder.HasIndex(item => item.SubjectConcurrencyKey).IsUnique()
            .HasFilter("\"Status\" IN (0, 1, 2, 3)")
            .HasDatabaseName("UX_DeploymentQueueTickets_SubjectConcurrencyKey");
        builder.HasIndex(item => new { item.Status, item.FairnessKey, item.CreatedAt })
            .HasDatabaseName("IX_DeploymentQueueTickets_Status_Fairness_Created");
        builder.HasIndex(item => new { item.Status, item.NotBeforeAt, item.CreatedAt, item.Id })
            .HasDatabaseName("IX_DeploymentQueueTickets_Status_NotBefore_Created_Id");
        builder.HasIndex(item => new { item.TargetNodeId, item.Status, item.CreatedAt, item.Id })
            .HasDatabaseName("IX_DeploymentQueueTickets_Node_Status_Created_Id");
        builder.HasIndex(item => new { item.Status, item.CompletedAt, item.Id })
            .IsDescending(false, true, true)
            .HasFilter("\"Status\" IN (4, 5, 6)")
            .HasDatabaseName("IX_DeploymentQueueTickets_Terminal_Completed_Id");
        builder.HasOne(item => item.TargetNode).WithMany().HasForeignKey(item => item.TargetNodeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class FleetCapacityReservationEntityConfiguration : IEntityTypeConfiguration<FleetCapacityReservation>
{
    public void Configure(EntityTypeBuilder<FleetCapacityReservation> builder)
    {
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasIndex(item => new { item.DeploymentQueueTicketId, item.WorkerNodeId }).IsUnique()
            .HasDatabaseName("UX_FleetCapacityReservations_Ticket_Node");
        builder.HasIndex(item => new { item.WorkerNodeId, item.Status, item.ExpiresAt })
            .HasDatabaseName("IX_FleetCapacityReservations_Node_Status_Expires");
        builder.HasIndex(item => new { item.DeploymentQueueTicketId, item.Status })
            .HasDatabaseName("IX_FleetCapacityReservations_Ticket_Status");
        builder.HasOne(item => item.DeploymentQueueTicket).WithMany()
            .HasForeignKey(item => item.DeploymentQueueTicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode).WithMany()
            .HasForeignKey(item => item.WorkerNodeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ImageDistributionRecordEntityConfiguration : IEntityTypeConfiguration<ImageDistributionRecord>
{
    public void Configure(EntityTypeBuilder<ImageDistributionRecord> builder)
    {
        builder.Property(item => item.ImageType).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.Operation).HasConversion<byte>();
        builder.Property(item => item.Stage).HasConversion<byte>();
        builder.Property(item => item.ErrorCategory).HasConversion<byte>();
        builder.HasIndex(item => new { item.ImageTemplateId, item.WorkerNodeId }).IsUnique()
            .HasDatabaseName("UX_ImageDistributionRecords_Template_Node");
        builder.HasIndex(item => new { item.WorkerNodeId, item.Status, item.LastCheckedAt })
            .HasDatabaseName("IX_ImageDistributionRecords_Node_Status_Checked");
        builder.HasIndex(item => new { item.Status, item.NextAttemptAt, item.ClaimExpiresAt, item.CreatedAt })
            .HasDatabaseName("IX_ImageDistributionRecords_Work_Claim");
        builder.HasOne(item => item.ImageTemplate).WithMany().HasForeignKey(item => item.ImageTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode).WithMany().HasForeignKey(item => item.WorkerNodeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.References).WithOne(item => item.DistributionRecord)
            .HasForeignKey(item => item.DistributionRecordId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImageDistributionReferenceEntityConfiguration : IEntityTypeConfiguration<ImageDistributionReference>
{
    public void Configure(EntityTypeBuilder<ImageDistributionReference> builder)
    {
        builder.ToTable("ImageDistributionReferences");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Kind).HasConversion<byte>();
        builder.HasIndex(item => new { item.DistributionRecordId, item.Kind, item.ResourceId })
            .IsUnique()
            .HasFilter("\"ResourcePublicId\" IS NULL")
            .HasDatabaseName("UX_ImageDistributionReferences_Record_Kind_Resource");
        builder.HasIndex(item => new { item.DistributionRecordId, item.Kind, item.ResourcePublicId })
            .IsUnique()
            .HasFilter("\"ResourcePublicId\" IS NOT NULL")
            .HasDatabaseName("UX_ImageDistributionReferences_Record_Kind_PublicResource");
        builder.HasIndex(item => new { item.Kind, item.ResourceId })
            .HasDatabaseName("IX_ImageDistributionReferences_Kind_Resource");
        builder.HasIndex(item => new { item.Kind, item.ResourcePublicId })
            .HasFilter("\"ResourcePublicId\" IS NOT NULL")
            .HasDatabaseName("IX_ImageDistributionReferences_Kind_PublicResource");
    }
}
