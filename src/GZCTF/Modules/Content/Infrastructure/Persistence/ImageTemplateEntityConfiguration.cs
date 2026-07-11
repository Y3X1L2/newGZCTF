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
        builder.HasOne(template => template.CreatedBy)
            .WithMany()
            .HasForeignKey(template => template.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
