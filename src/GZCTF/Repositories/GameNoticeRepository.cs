using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Repositories.Interface;
using GZCTF.Infrastructure.Cache;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;


namespace GZCTF.Repositories;

public class GameNoticeRepository(
    IPlatformCache cache,
    IHubContext<UserHub, IUserClient> hub,
    AppDbContext context) : RepositoryBase(context), IGameNoticeRepository
{
    public async Task<GameNotice> AddNotice(GameNotice notice, CancellationToken token = default)
    {
        await Context.AddAsync(notice, token);
        await SaveAsync(token);

        await cache.InvalidateAsync(CachePolicyCatalog.GameNotices, notice.GameId.ToString(), token);

        await hub.Clients.Group($"Game_{notice.GameId}").ReceivedGameNotice(notice);

        return notice;
    }

    public Task<GameNotice[]> GetNormalNotices(int gameId, CancellationToken token = default) =>
        Context.GameNotices
            .Where(n => n.GameId == gameId && n.Type == NoticeType.Normal)
            .ToArrayAsync(token);

    public Task<GameNotice?> GetNoticeById(int gameId, int noticeId, CancellationToken token = default) =>
        Context.GameNotices.FirstOrDefaultAsync(e => e.Id == noticeId && e.GameId == gameId, token);

    public Task<DataWithModifiedTime<GameNotice[]>> GetLatestNotices(int gameId, CancellationToken token = default)
        => cache.GetOrCreateAsync(CachePolicyCatalog.GameNotices, gameId.ToString(), async ct =>
        {
            var notices = await Context.GameNotices.Where(e => e.GameId == gameId)
                .OrderByDescending(e => e.Type == NoticeType.Normal ? DateTimeOffset.UtcNow : e.PublishTimeUtc)
                .Take(300).ToArrayAsync(ct);
            return new DataWithModifiedTime<GameNotice[]>(notices, DateTimeOffset.UtcNow);
        }, token).AsTask();

    public async Task RemoveNotice(GameNotice notice, CancellationToken token = default)
    {
        Context.Remove(notice);
        await SaveAsync(token);

        await cache.InvalidateAsync(CachePolicyCatalog.GameNotices, notice.GameId.ToString(), token);
    }

    public async Task<GameNotice> UpdateNotice(GameNotice notice, CancellationToken token = default)
    {
        notice.PublishTimeUtc = DateTimeOffset.UtcNow;
        await SaveAsync(token);

        await cache.InvalidateAsync(CachePolicyCatalog.GameNotices, notice.GameId.ToString(), token);

        return notice;
    }
}
