namespace GZCTF.Models.Request.Exercise;

public class ExerciseImportFromGameModel
{
    public int GameId { get; set; }
    public int[]? ChallengeIds { get; set; }
}

public class ExerciseImportFromTrainingModel
{
    public int CourseId { get; set; }
    public int[]? ChallengeIds { get; set; }
}
