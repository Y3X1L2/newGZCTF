using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabReleaseAssetArtifactEntityConfiguration
    : IEntityTypeConfiguration<TeamLabReleaseAssetArtifact>
{
    public void Configure(EntityTypeBuilder<TeamLabReleaseAssetArtifact> builder)
    {
        builder.ToTable("TeamLabReleaseAssetArtifacts");
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasIndex(item => new { item.ReleaseId, item.AssetKey }).IsUnique();
        builder.HasIndex(item => item.BuildIdentity);
        builder.HasIndex(item => item.BakeRuntimeId);
        builder.HasOne(item => item.Release)
            .WithMany()
            .HasForeignKey(item => item.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.SourceImageTemplate)
            .WithMany()
            .HasForeignKey(item => item.SourceImageTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.ScenarioImageTemplate)
            .WithMany()
            .HasForeignKey(item => item.ScenarioImageTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.BakeRuntime)
            .WithMany()
            .HasForeignKey(item => item.BakeRuntimeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
