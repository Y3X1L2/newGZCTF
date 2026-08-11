namespace GZCTF.Models.Request.Exercise;

public class ExerciseFilter
{
    public string? Search { get; set; }
    public ChallengeCategory[]? Categories { get; set; }
    public Difficulty[]? Difficulties { get; set; }
    public string[]? Tags { get; set; }
    public bool? Credit { get; set; }
    public ExercisePoolSource[]? Sources { get; set; }
}
