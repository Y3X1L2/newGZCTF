namespace GZCTF.Modules.Exercise.Application;

public interface IExerciseManagementService
{
    Task<ExerciseChallenge> CreateExerciseAsync(ExerciseChallenge exercise, CancellationToken token = default);
    Task<ExerciseChallenge> UpdateExerciseAsync(ExerciseChallenge exercise, CancellationToken token = default);
    Task RemoveExerciseAsync(int exerciseId, CancellationToken token = default);
    Task<ExerciseChallenge> ImportFromGameChallengeAsync(int gameChallengeId, CancellationToken token = default);
    Task<ExerciseChallenge[]> ImportFromGameAsync(int gameId, int[]? challengeIds = null, CancellationToken token = default);
}
