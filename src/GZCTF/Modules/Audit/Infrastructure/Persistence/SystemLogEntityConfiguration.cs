using GZCTF.Models.Data;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Audit.Infrastructure.Persistence;

public sealed class SystemLogEntityConfiguration : IEntityTypeConfiguration<LogModel>
{
    public void Configure(EntityTypeBuilder<LogModel> builder)
    {
        builder.HasKey(item => new { item.TimeUtc, item.Id });
        builder.Property(item => item.Id).ValueGeneratedOnAdd();
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(Limits.MaxLogStatusLength);
        builder.HasIndex(item => new { item.CorrelationId, item.TimeUtc, item.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Logs_Correlation_Time_Id");
        builder.HasIndex(item => new { item.EventCode, item.TimeUtc, item.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Logs_Event_Time_Id");
        builder.HasIndex(item => new { item.WorkerNodeId, item.TimeUtc, item.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Logs_Node_Time_Id");
        builder.HasIndex(item => new { item.TimeUtc, item.Id }).IsDescending(true, true)
            .HasDatabaseName("IX_Logs_Time_Id");
        builder.HasIndex(item => new { item.Level, item.TimeUtc, item.Id }).IsDescending(false, true, true)
            .HasDatabaseName("IX_Logs_Level_Time_Id");
    }
}

public sealed class OperationalLogAggregateEntityConfiguration : IEntityTypeConfiguration<Domain.OperationalLogAggregate>
{
    public void Configure(EntityTypeBuilder<Domain.OperationalLogAggregate> builder)
    {
        builder.ToTable("OperationalLogAggregates");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Level).HasMaxLength(Limits.MaxLogLevelLength).IsRequired();
        builder.Property(item => item.Logger).HasMaxLength(Limits.MaxLoggerLength).IsRequired();
        builder.HasIndex(item => new { item.BucketStart, item.Level, item.Logger }).IsUnique()
            .HasDatabaseName("UX_OperationalLogAggregates_Bucket_Level_Logger");
    }
}

public sealed class DeploymentLifecycleAggregateEntityConfiguration : IEntityTypeConfiguration<Domain.DeploymentLifecycleAggregate>
{
    public void Configure(EntityTypeBuilder<Domain.DeploymentLifecycleAggregate> builder)
    {
        builder.ToTable("DeploymentLifecycleAggregates");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.BucketStart, item.Kind, item.Status, item.WorkerNodeId }).IsUnique()
            .HasDatabaseName("UX_DeploymentLifecycleAggregates_Dimensions");
    }
}

public sealed class DataGovernanceRunEntityConfiguration : IEntityTypeConfiguration<Domain.DataGovernanceRun>
{
    public void Configure(EntityTypeBuilder<Domain.DataGovernanceRun> builder)
    {
        builder.ToTable("DataGovernanceRuns");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.DataSet).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Operation).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.LeaseOwner).HasMaxLength(128).IsRequired();
        builder.Property(item => item.PartitionName).HasMaxLength(128);
        builder.Property(item => item.ErrorCode).HasMaxLength(128);
        builder.Property(item => item.ErrorDetail).HasMaxLength(2048);
        builder.HasIndex(item => new { item.DataSet, item.StartedAt, item.Id })
            .IsDescending(false, true, true);
        builder.HasIndex(item => new { item.Status, item.CompletedAt, item.Id });
    }
}
