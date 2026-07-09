using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(ImageTemplateId), nameof(WorkerNodeId), IsUnique = true)]
[Index(nameof(Status))]
public class ImageDistributionRecord
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int ImageTemplateId { get; set; }

    public Guid WorkerNodeId { get; set; }

    [MaxLength(128)]
    public string ImageHash { get; set; } = string.Empty;

    public ImageType ImageType { get; set; }

    public ImageDistributionStatus Status { get; set; } = ImageDistributionStatus.Pending;

    public int ReferenceCount { get; set; }

    public List<ImageDistributionReference> References { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastCheckedAt { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    [ForeignKey(nameof(ImageTemplateId))]
    [JsonIgnore]
    public ImageTemplate? ImageTemplate { get; set; }

    [ForeignKey(nameof(WorkerNodeId))]
    [JsonIgnore]
    public WorkerNode? WorkerNode { get; set; }
}

public enum ImageDistributionStatus : byte
{
    Pending = 0,
    Pulling = 1,
    Ready = 2,
    Failed = 3,
    CleanupPending = 4
}

public sealed record ImageDistributionReference(
    ImageDistributionReferenceKind Kind,
    int Id)
{
    public static ImageDistributionReference Game(int gameId) =>
        new(ImageDistributionReferenceKind.Game, gameId);

    public static ImageDistributionReference TrainingCourse(int courseId) =>
        new(ImageDistributionReferenceKind.TrainingCourse, courseId);
}

public enum ImageDistributionReferenceKind : byte
{
    Game = 0,
    TrainingCourse = 1
}
