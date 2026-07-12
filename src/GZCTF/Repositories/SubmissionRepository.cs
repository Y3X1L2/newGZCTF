using GZCTF.Extensions;
using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Models.Request.Game;

namespace GZCTF.Repositories;

public class SubmissionRepository(
    IHubContext<MonitorHub, IMonitorClient> hub,
    AppDbContext context) : RepositoryBase(context), ISubmissionRepository
{
    public async Task<Submission> AddSubmission(Submission submission, CancellationToken token = default)
    {
        await Context.AddAsync(submission, token);
        await Context.SaveChangesAsync(token);

        return submission;
    }

    public Task<Submission?> GetSubmission(int gameId, int challengeId, Guid userId, int submitId,
        CancellationToken token = default)
        => Context.Submissions.IgnoreAutoIncludes().Where(s =>
                s.Id == submitId && s.UserId == userId && s.GameId == gameId && s.ChallengeId == challengeId)
            .SingleOrDefaultAsync(token);

    public Task<int> CountSubmissions(int participationId, int challengeId, CancellationToken token = default) =>
        Context.Submissions.CountAsync(s =>
            s.ParticipationId == participationId && s.ChallengeId == challengeId, token);

    public Task<Submission[]> GetUncheckedFlags(CancellationToken token = default) =>
        Context.Submissions.Where(s => s.Status == AnswerResult.FlagSubmitted)
            .AsNoTracking().Include(e => e.Game).ToArrayAsync(token);

    public async Task<SubmissionPageModel> GetSubmissions(
        Game game,
        AnswerResult? type = null,
        int count = 100,
        string? cursor = null,
        CancellationToken token = default)
    {
        var take = Math.Clamp(count, 1, 100);
        var query = GetSubmissionsByType(type).Where(item => item.GameId == game.Id);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var decoded = TimeCursor.Decode(cursor);
            query = query.Where(item => item.SubmitTimeUtc < decoded.Time ||
                                        item.SubmitTimeUtc == decoded.Time && item.Id < decoded.Id);
        }

        var rows = await query.Take(take + 1).ToArrayAsync(token);
        var items = rows.Take(take).ToArray();
        var next = rows.Length > take && items.Length > 0
            ? new TimeCursor(items[^1].SubmitTimeUtc, items[^1].Id).Encode()
            : null;
        return new SubmissionPageModel(items, next);
    }

    public Task<Submission[]> GetAllSubmissions(Game game, AnswerResult? type = null,
        CancellationToken token = default) =>
        GetSubmissionsByType(type).Where(item => item.GameId == game.Id).ToArrayAsync(token);

    public Task SendSubmission(Submission submission)
        => hub.Clients.Group($"Game_{submission.GameId}").ReceivedSubmissions(submission);


    private IQueryable<Submission> GetSubmissionsByType(AnswerResult? type = null)
    {
        var subs = type is not null
            ? Context.Submissions.Where(s => s.Status == type.Value)
            : Context.Submissions;

        return subs.AsNoTracking().OrderByDescending(s => s.SubmitTimeUtc).ThenByDescending(s => s.Id);
    }
}
