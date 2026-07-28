using GZCTF.Models.Request.Exercise;

namespace GZCTF.Modules.Exercise.Application;

public interface IExerciseService
{
    Task<ExerciseChallenge[]> GetExercisesAsync(CancellationToken token = default);
    Task<ExerciseChallenge?> GetExerciseByIdAsync(int exerciseId, CancellationToken token = default);
    Task<ExerciseInfoModel[]> GetExerciseListAsync(ExerciseFilter? filter, CancellationToken token = default);
    Task<ExerciseDetailModel?> GetExerciseDetailAsync(UserInfo user, int exerciseId, CancellationToken token = default);
    Task<(AnswerResult Status, int? FlagId)> SubmitFlagAsync(UserInfo user, int exerciseId, string answer, int? flagId = null, CancellationToken token = default);
    Task<TaskResult<Container>> CreateContainerAsync(UserInfo user, int exerciseId, CancellationToken token = default);
}
