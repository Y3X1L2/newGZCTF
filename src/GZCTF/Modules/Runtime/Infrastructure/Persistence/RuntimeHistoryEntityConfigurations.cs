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
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasIndex(item => item.ActiveIdentity).IsUnique()
            .HasFilter("\"Status\" IN (0, 1, 2)")
            .HasDatabaseName("UX_DeploymentQueueTickets_ActiveIdentity");
        builder.HasIndex(item => new { item.Status, item.CreatedAt, item.Id })
            .HasDatabaseName("IX_DeploymentQueueTickets_Status_Created_Id");
        builder.HasIndex(item => new { item.TargetNodeId, item.Status, item.CreatedAt, item.Id })
            .HasDatabaseName("IX_DeploymentQueueTickets_Node_Status_Created_Id");
        builder.HasIndex(item => new { item.Status, item.CompletedAt, item.Id })
            .IsDescending(false, true, true)
            .HasFilter("\"Status\" IN (3, 4, 5)")
            .HasDatabaseName("IX_DeploymentQueueTickets_Terminal_Completed_Id");
        builder.HasOne(item => item.DeploymentTarget).WithMany().HasForeignKey(item => item.DeploymentTargetId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.TargetNode).WithMany().HasForeignKey(item => item.TargetNodeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class ImageDistributionRecordEntityConfiguration : IEntityTypeConfiguration<ImageDistributionRecord>
{
    public void Configure(EntityTypeBuilder<ImageDistributionRecord> builder)
    {
        builder.Property(item => item.ImageType).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasIndex(item => new { item.ImageTemplateId, item.WorkerNodeId }).IsUnique()
            .HasDatabaseName("UX_ImageDistributionRecords_Template_Node");
        builder.HasIndex(item => new { item.WorkerNodeId, item.Status, item.LastCheckedAt })
            .HasDatabaseName("IX_ImageDistributionRecords_Node_Status_Checked");
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
            .IsUnique().HasDatabaseName("UX_ImageDistributionReferences_Record_Kind_Resource");
        builder.HasIndex(item => new { item.Kind, item.ResourceId })
            .HasDatabaseName("IX_ImageDistributionReferences_Kind_Resource");
    }
}
