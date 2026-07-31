using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Content.Infrastructure.Persistence;

public sealed class ImageTemplateEntityConfiguration : IEntityTypeConfiguration<ImageTemplate>
{
    public void Configure(EntityTypeBuilder<ImageTemplate> builder)
    {
        builder.HasIndex(template => template.Name);
        builder.HasIndex(template => template.Status);
        builder.HasIndex(template => template.CreatedById);
        builder.Property(template => template.VmArtifactStatus).HasConversion<byte>();
        builder.HasIndex(template => template.PreparedArtifactId).IsUnique();
        builder.HasOne(template => template.CreatedBy)
            .WithMany()
            .HasForeignKey(template => template.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(template => template.RemoteAccess)
            .WithOne(item => item.ImageTemplate)
            .HasForeignKey<ImageTemplateRemoteAccess>(item => item.ImageTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImageTemplateRemoteAccessEntityConfiguration : IEntityTypeConfiguration<ImageTemplateRemoteAccess>
{
    public void Configure(EntityTypeBuilder<ImageTemplateRemoteAccess> builder)
    {
        builder.ToTable("ImageTemplateRemoteAccesses", table =>
            table.HasCheckConstraint("CK_ImageTemplateRemoteAccesses_Port", "\"Port\" >= 1 AND \"Port\" <= 65535"));
        builder.HasKey(item => item.ImageTemplateId);
        builder.Property(item => item.Protocol).HasConversion<byte>();
        builder.Property(item => item.CredentialMode).HasConversion<byte>();
    }
}
