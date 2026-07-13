namespace GZCTF.Modules.Runtime.Domain;

public sealed class ImageDistributionReference
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DistributionRecordId { get; set; }
    public ImageDistributionReferenceKind Kind { get; set; }
    public int ResourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ImageDistributionRecord DistributionRecord { get; set; } = null!;
}

public enum ImageDistributionReferenceKind : byte
{
    Game = 0,
    TrainingCourse = 1,
    Exercise = 2,
    TeamLabRuntime = 3
}

public readonly record struct ImageDistributionReferenceKey(
    ImageDistributionReferenceKind Kind,
    int ResourceId)
{
    public static ImageDistributionReferenceKey Game(int gameId) =>
        new(ImageDistributionReferenceKind.Game, gameId);

    public static ImageDistributionReferenceKey TrainingCourse(int courseId) =>
        new(ImageDistributionReferenceKind.TrainingCourse, courseId);

    public static ImageDistributionReferenceKey Exercise(int exerciseId) =>
        new(ImageDistributionReferenceKind.Exercise, exerciseId);

    public static ImageDistributionReferenceKey TeamLabRuntime(int runtimeId) =>
        new(ImageDistributionReferenceKind.TeamLabRuntime, runtimeId);
}
