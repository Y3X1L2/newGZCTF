namespace GZCTF.Modules.Exercise.Domain;

public enum ExerciseMutationKind
{
    Create = 0,
    Import = 1,
    Update = 2,
    Delete = 3
}

public sealed class ExerciseMutationJob
{
    public Guid OperationId { get; set; }
    public ExerciseMutationKind Kind { get; set; }
    public int? ExerciseId { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
