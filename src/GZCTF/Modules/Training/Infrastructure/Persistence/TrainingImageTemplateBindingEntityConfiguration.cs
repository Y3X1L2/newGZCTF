using GZCTF.Modules.Training.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Training.Infrastructure.Persistence;

public sealed class TrainingImageTemplateBindingEntityConfiguration
    : IEntityTypeConfiguration<TrainingCourseImageTemplateBinding>
{
    public void Configure(EntityTypeBuilder<TrainingCourseImageTemplateBinding> builder)
    {
        builder.ToTable("TrainingCourseImageTemplateBindings");
        builder.HasKey(binding => new { binding.CourseId, binding.ImageTemplateId });
        builder.HasIndex(binding => binding.ImageTemplateId);
        builder.HasOne<TrainingCourse>()
            .WithMany()
            .HasForeignKey(binding => binding.CourseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ImageTemplate>()
            .WithMany()
            .HasForeignKey(binding => binding.ImageTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserInfo>()
            .WithMany()
            .HasForeignKey(binding => binding.AddedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
