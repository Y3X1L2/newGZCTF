using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Content.Infrastructure.Persistence;

public sealed class BootstrapProfileEntityConfiguration : IEntityTypeConfiguration<BootstrapProfile>
{
    public void Configure(EntityTypeBuilder<BootstrapProfile> builder)
    {
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasOne(item => item.CreatedBy).WithMany().HasForeignKey(item => item.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Versions).WithOne(item => item.Profile).HasForeignKey(item => item.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BootstrapProfileVersionEntityConfiguration : IEntityTypeConfiguration<BootstrapProfileVersion>
{
    public void Configure(EntityTypeBuilder<BootstrapProfileVersion> builder)
    {
        builder.HasIndex(item => new { item.ProfileId, item.Version }).IsUnique();
        builder.HasIndex(item => item.ArtifactDigest);
        builder.Property(item => item.ManifestJson).HasColumnType("jsonb");
        builder.HasOne(item => item.CreatedBy).WithMany().HasForeignKey(item => item.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Distributions).WithOne(item => item.ProfileVersion)
            .HasForeignKey(item => item.ProfileVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BootstrapProfileOperationJobEntityConfiguration :
    IEntityTypeConfiguration<BootstrapProfileOperationJob>
{
    public void Configure(EntityTypeBuilder<BootstrapProfileOperationJob> builder)
    {
        builder.HasIndex(item => item.OperationId).IsUnique();
        builder.Property(item => item.ManifestJson).HasColumnType("jsonb");
        builder.HasOne(item => item.Operation).WithOne().HasForeignKey<BootstrapProfileOperationJob>(item => item.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BootstrapProfileDistributionEntityConfiguration :
    IEntityTypeConfiguration<BootstrapProfileDistribution>
{
    public void Configure(EntityTypeBuilder<BootstrapProfileDistribution> builder)
    {
        builder.HasIndex(item => new { item.ProfileVersionId, item.WorkerNodeId }).IsUnique();
        builder.HasOne(item => item.WorkerNode).WithMany().HasForeignKey(item => item.WorkerNodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImageTemplateCapabilityCertificationEntityConfiguration :
    IEntityTypeConfiguration<ImageTemplateCapabilityCertification>
{
    public void Configure(EntityTypeBuilder<ImageTemplateCapabilityCertification> builder)
    {
        builder.HasIndex(item => new { item.ImageTemplateId, item.ImageHash, item.EvidenceDigest }).IsUnique();
        builder.Property(item => item.CapabilitiesJson).HasColumnType("jsonb");
        builder.HasIndex(item => new
        {
            item.ImageTemplateId,
            item.PreparationContractVersion,
            item.GuestProtocolVersion
        });
        builder.HasOne(item => item.ImageTemplate).WithMany(item => item.CapabilityCertifications)
            .HasForeignKey(item => item.ImageTemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.WorkerNode).WithMany().HasForeignKey(item => item.WorkerNodeId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.CertifiedBy).WithMany().HasForeignKey(item => item.CertifiedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ImageTemplateCertificationJobEntityConfiguration :
    IEntityTypeConfiguration<ImageTemplateCertificationJob>
{
    public void Configure(EntityTypeBuilder<ImageTemplateCertificationJob> builder)
    {
        builder.HasIndex(item => item.OperationId).IsUnique();
        builder.Property(item => item.CapabilitiesJson).HasColumnType("jsonb");
        builder.HasOne(item => item.Operation).WithOne().HasForeignKey<ImageTemplateCertificationJob>(item => item.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
