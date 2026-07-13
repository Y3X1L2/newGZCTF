using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Audit.Infrastructure.Persistence;

public sealed class OperationalEventEntityConfiguration : IEntityTypeConfiguration<OperationalEvent>
{
    public void Configure(EntityTypeBuilder<OperationalEvent> builder)
    {
        builder.ToTable("OperationalEvents");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.EventCode).HasMaxLength(128).IsRequired();
        builder.Property(item => item.TraceId).HasMaxLength(64);
        builder.Property(item => item.Severity).HasConversion<byte>();
        builder.Property(item => item.Outcome).HasConversion<byte>();
        builder.Property(item => item.ErrorCategory).HasConversion<byte>();
        builder.Property(item => item.ErrorCode).HasMaxLength(128);
        builder.Property(item => item.Message).HasMaxLength(1024).IsRequired();
        builder.Property(item => item.DetailJson).HasMaxLength(4096);
        builder.Property(item => item.SubjectType).HasMaxLength(64);
        builder.Property(item => item.SubjectId).HasMaxLength(128);
        builder.Property(item => item.SubjectDisplayName).HasMaxLength(256);
        builder.Property(item => item.ResourceType).HasMaxLength(64);
        builder.Property(item => item.ResourceId).HasMaxLength(128);
        builder.Property(item => item.ResourceDisplayName).HasMaxLength(256);
        builder.HasIndex(item => new { item.OccurredAt, item.Id }).IsDescending(true, true)
            .HasDatabaseName("IX_OperationalEvents_Time_Id");
        builder.HasIndex(item => new { item.CorrelationId, item.OccurredAt, item.Id })
            .HasDatabaseName("IX_OperationalEvents_Correlation_Time_Id");
        builder.HasIndex(item => new { item.DeploymentTicketId, item.OccurredAt, item.Id })
            .HasDatabaseName("IX_OperationalEvents_Ticket_Time_Id");
        builder.HasIndex(item => new { item.WorkerNodeId, item.OccurredAt, item.Id })
            .HasDatabaseName("IX_OperationalEvents_Node_Time_Id");
        builder.HasIndex(item => new { item.EventCode, item.Outcome, item.OccurredAt, item.Id })
            .HasDatabaseName("IX_OperationalEvents_Code_Outcome_Time_Id");
        builder.HasIndex(item => new { item.OwnerTeamId, item.OccurredAt, item.Id })
            .HasDatabaseName("IX_OperationalEvents_Team_Time_Id");
        builder.HasIndex(item => new { item.GameId, item.OccurredAt, item.Id })
            .HasDatabaseName("IX_OperationalEvents_Game_Time_Id");
        builder.HasIndex(item => new { item.CourseId, item.OccurredAt, item.Id })
            .HasDatabaseName("IX_OperationalEvents_Course_Time_Id");
        builder.HasIndex(item => new { item.ImageTemplateId, item.OccurredAt, item.Id })
            .HasDatabaseName("IX_OperationalEvents_Template_Time_Id");
    }
}
