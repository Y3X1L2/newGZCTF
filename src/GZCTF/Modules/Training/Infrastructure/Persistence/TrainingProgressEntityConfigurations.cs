using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Training.Infrastructure.Persistence;

public sealed class TrainingCourseProgressEntityConfiguration : IEntityTypeConfiguration<TrainingCourseProgress>
{
    public void Configure(EntityTypeBuilder<TrainingCourseProgress> builder)
    {
        builder.HasKey(item => new { item.CourseId, item.UserId });
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(item => new { item.CourseId, item.Status, item.UpdatedAt, item.UserId })
            .IsDescending(false, false, true, false)
            .HasDatabaseName("IX_TrainingCourseProgress_Course_Status_Updated_User");
        builder.HasIndex(item => new { item.UserId, item.UpdatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TrainingCourseProgress_User_Updated");
        builder.HasOne(item => item.Course).WithMany().HasForeignKey(item => item.CourseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TrainingChapterProgressEntityConfiguration : IEntityTypeConfiguration<TrainingChapterProgress>
{
    public void Configure(EntityTypeBuilder<TrainingChapterProgress> builder)
    {
        builder.HasKey(item => new { item.ChapterId, item.UserId });
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(item => new { item.UserId, item.UpdatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TrainingChapterProgress_User_Updated");
        builder.HasOne(item => item.Chapter).WithMany().HasForeignKey(item => item.ChapterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
