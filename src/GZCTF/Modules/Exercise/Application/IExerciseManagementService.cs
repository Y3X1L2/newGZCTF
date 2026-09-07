using GZCTF.Models.Data;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Exercise;

namespace GZCTF.Modules.Exercise.Application;

public sealed record ExerciseManagementPage(
    IReadOnlyList<ExerciseChallenge> Items,
    bool HasMore);

public interface IExerciseManagementService
{
    Task<ExerciseChallenge?> CollectGameChallengeAsync(int gameChallengeId, CancellationToken token = default);
    Task<ExerciseChallenge?> CollectTrainingChallengeAsync(int exerciseChallengeId, CancellationToken token = default);
    Task<ExerciseChallenge?> CollectAwdpServiceAsync(int awdpServiceId, CancellationToken token = default);
    Task<ExercisePoolBackfillResult> BackfillPoolAsync(CancellationToken token = default);
    Task<ExerciseChallenge> CreateExerciseAsync(ExerciseChallenge exercise, CancellationToken token = default);
    Task<ExerciseChallenge> CreateExerciseWithRelationsAsync(
        ExerciseChallenge exercise,
        List<ExerciseFlagCreateModel>? flags,
        AttachmentCreateModel? attachment,
        CancellationToken token = default);
    Task<ExerciseChallenge> UpdateExerciseAsync(ExerciseChallenge exercise, CancellationToken token = default);
    Task RemoveExerciseAsync(int exerciseId, CancellationToken token = default);
    Task<ExerciseChallenge> ImportFromGameChallengeAsync(int gameChallengeId, CancellationToken token = default);
    Task<ExerciseChallenge[]> ImportFromGameAsync(int gameId, int[]? challengeIds = null, CancellationToken token = default);
    Task<ExerciseChallenge[]> ImportFromTrainingAsync(int courseId, int[]? challengeIds = null, CancellationToken token = default);

    Task<ExerciseChallenge?> GetExerciseForUpdateAsync(int exerciseId, CancellationToken token = default);
    Task<ExerciseInfoModel[]> GetExerciseManagementListAsync(CancellationToken token = default);
    Task<ExerciseManagementPage> GetExercisePageAsync(
        ExerciseFilter? filter,
        int limit,
        int? afterId,
        CancellationToken token = default);
    Task<ExerciseChallenge> UpdateExerciseWithRelationsAsync(
        ExerciseChallenge exercise,
        List<ExerciseOpenApiFlagModel>? flags,
        ExerciseOpenApiAttachmentModel? attachment,
        CancellationToken token = default);
    Task<ExerciseChallenge> UpdateExerciseWithRelationsAsync(
        ExerciseChallenge exercise,
        List<ExerciseFlagCreateModel>? flags,
        AttachmentCreateModel? attachment,
        CancellationToken token = default);
}
