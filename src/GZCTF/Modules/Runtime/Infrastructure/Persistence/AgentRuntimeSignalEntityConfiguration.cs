using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Runtime.Infrastructure.Persistence;

public sealed class AgentRuntimeSignalEntityConfiguration : IEntityTypeConfiguration<AgentRuntimeSignal>
{
    public void Configure(EntityTypeBuilder<AgentRuntimeSignal> builder)
    {
        builder.ToTable("AgentRuntimeSignals");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Stage).HasConversion<byte>();
        builder.Property(item => item.Outcome).HasConversion<byte>();
        builder.Property(item => item.ResourceKind).HasMaxLength(64);
        builder.Property(item => item.ResourceId).HasMaxLength(256);
        builder.Property(item => item.ErrorCode).HasMaxLength(128);
        builder.Property(item => item.PayloadHash).HasMaxLength(64);
        builder.Property(item => item.FactsJson).HasColumnType("jsonb");
        builder.HasIndex(item => new { item.WorkerNodeId, item.OperationId, item.Sequence })
            .IsUnique()
            .HasDatabaseName("UX_AgentRuntimeSignals_Node_Operation_Sequence");
        builder.HasIndex(item => new { item.OperationId, item.Generation, item.Sequence })
            .HasDatabaseName("IX_AgentRuntimeSignals_Operation_Generation_Sequence");
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.ReceivedAt })
            .HasDatabaseName("IX_AgentRuntimeSignals_Runtime_Generation_Received");
        builder.HasOne(item => item.WorkerNode).WithMany()
            .HasForeignKey(item => item.WorkerNodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Runtime).WithMany()
            .HasForeignKey(item => item.RuntimeId).OnDelete(DeleteBehavior.Cascade);
    }
}
