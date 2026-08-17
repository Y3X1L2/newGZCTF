namespace GZCTF.Modules.Runtime.Domain;

public sealed class ImageDistributionReference
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DistributionRecordId { get; set; }
    public ImageDistributionReferenceKind Kind { get; set; }
    public int ResourceId { get; set; }
    public Guid? ResourcePublicId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ImageDistributionRecord DistributionRecord { get; set; } = null!;
}

public enum ImageDistributionReferenceKind : byte
{
    Game = 0,
    TrainingCourse = 1,
    Exercise = 2,
    TeamLabRuntime = 3,
    ImageCertification = 4,
    TeamLabRollout = 5,
    TeamLabTopology = 6,
    TeamLabRelease = 7,

    // Purpose aliases retain the persisted values used by existing TeamLab references while
    // making the cache lifetime explicit to new execution-plane callers.
    Runtime = TeamLabRuntime,
    CompetitionPreparation = TeamLabRelease,
    Rollout = TeamLabRollout,
    ArtifactVerification = ImageCertification
}

public readonly record struct ImageDistributionReferenceKey(
    ImageDistributionReferenceKind Kind,
    int ResourceId,
    Guid? ResourcePublicId = null)
{
    public static ImageDistributionReferenceKey Game(int gameId) =>
        new(ImageDistributionReferenceKind.Game, gameId);

    public static ImageDistributionReferenceKey TrainingCourse(int courseId) =>
        new(ImageDistributionReferenceKind.TrainingCourse, courseId);

    public static ImageDistributionReferenceKey Exercise(int exerciseId) =>
        new(ImageDistributionReferenceKind.Exercise, exerciseId);

    public static ImageDistributionReferenceKey TeamLabRuntime(int runtimeId) =>
        new(ImageDistributionReferenceKind.TeamLabRuntime, runtimeId);

    public static ImageDistributionReferenceKey ImageCertification(int imageTemplateId) =>
        new(ImageDistributionReferenceKind.ImageCertification, imageTemplateId);

    public static ImageDistributionReferenceKey TeamLabRollout(int rolloutId) =>
        new(ImageDistributionReferenceKind.TeamLabRollout, rolloutId);

    public static ImageDistributionReferenceKey TeamLabTopology(int topologyId) =>
        new(ImageDistributionReferenceKind.TeamLabTopology, topologyId);

    public static ImageDistributionReferenceKey TeamLabRelease(Guid releaseId) =>
        new(ImageDistributionReferenceKind.TeamLabRelease, 0, releaseId);

    public static ImageDistributionReferenceKey Runtime(int runtimeId) => TeamLabRuntime(runtimeId);

    public static ImageDistributionReferenceKey CompetitionPreparation(Guid releaseId) => TeamLabRelease(releaseId);

    public static ImageDistributionReferenceKey Rollout(int rolloutId) => TeamLabRollout(rolloutId);

    public static ImageDistributionReferenceKey ArtifactVerification(int imageTemplateId) =>
        ImageCertification(imageTemplateId);
}
