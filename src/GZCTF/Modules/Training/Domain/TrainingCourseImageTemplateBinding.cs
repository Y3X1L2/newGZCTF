namespace GZCTF.Modules.Training.Domain;

public sealed class TrainingCourseImageTemplateBinding
{
    public int CourseId { get; set; }
    public int ImageTemplateId { get; set; }
    public Guid? AddedById { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
