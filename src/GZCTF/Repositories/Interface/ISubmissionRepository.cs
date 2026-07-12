using GZCTF.Models.Request.Game;

namespace GZCTF.Repositories.Interface;

public interface ISubmissionRepository : IRepository
{
    Task<SubmissionPageModel> GetSubmissions(
        Game game,
        AnswerResult? type = null,
        int count = 100,
        string? cursor = null,
        CancellationToken token = default);

    Task<Submission[]> GetAllSubmissions(
        Game game,
        AnswerResult? type = null,
        CancellationToken token = default);

    Task SendSubmission(Submission submission);
    Task<Submission> AddSubmission(Submission submission, CancellationToken token = default);
    Task<Submission[]> GetUncheckedFlags(CancellationToken token = default);
    Task<Submission?> GetSubmission(int gameId, int challengeId, Guid userId, int submitId,
        CancellationToken token = default);
    Task<int> CountSubmissions(int participationId, int challengeId, CancellationToken token = default);
}
