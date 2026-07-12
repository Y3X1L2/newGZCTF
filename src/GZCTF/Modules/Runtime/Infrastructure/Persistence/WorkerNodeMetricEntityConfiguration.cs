using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Runtime.Infrastructure.Persistence;

public sealed class WorkerNodeMetricEntityConfiguration : IEntityTypeConfiguration<WorkerNodeMetricSample>
{
    public void Configure(EntityTypeBuilder<WorkerNodeMetricSample> builder)
    {
        builder.ToTable("WorkerNodeMetricSamples");
        builder.HasKey(sample => new { sample.WorkerNodeId, sample.WindowStart });
        builder.HasIndex(sample => new { sample.WindowStart, sample.WorkerNodeId })
            .HasDatabaseName("IX_WorkerNodeMetricSamples_Window_Node");
        builder.HasOne<WorkerNode>().WithMany().HasForeignKey(sample => sample.WorkerNodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
