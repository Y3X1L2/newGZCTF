namespace GZCTF.Models.Request.Exercise;

public class ExerciseCreateModel
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;
    public Difficulty Difficulty { get; set; } = Difficulty.Baby;
    public bool Credit { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Hints { get; set; }
    public string? ContainerImage { get; set; }
    public int? MemoryLimit { get; set; } = 64;
    public int? StorageLimit { get; set; } = 256;
    public int? CPUCount { get; set; } = 1;
    public int? ExposePort { get; set; } = 80;
    public string? FlagTemplate { get; set; }
}
