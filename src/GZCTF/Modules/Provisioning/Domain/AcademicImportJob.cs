namespace GZCTF.Modules.Provisioning.Domain;

public enum AcademicImportKind
{
    TrainingCourses = 0,
    TheoryQuestions = 1,
    TheoryPaper = 2,
    Teams = 3
}

public sealed class AcademicImportJob
{
    public Guid OperationId { get; set; }
    public AcademicImportKind Kind { get; set; }
    public int? TargetId { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
