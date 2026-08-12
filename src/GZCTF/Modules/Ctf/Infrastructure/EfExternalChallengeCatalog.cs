using GZCTF.Models;
using GZCTF.Modules.Ctf.Application;
using GZCTF.Modules.Ctf.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Ctf.Infrastructure;

public sealed class EfExternalChallengeCatalog(AppDbContext context) : IExternalChallengeCatalog
{
    public Task<bool> GameExistsAsync(int gameId, CancellationToken cancellationToken) =>
        context.Games.AsNoTracking().AnyAsync(game => game.Id == gameId, cancellationToken);

    public async Task<IReadOnlySet<int>> FindReadyWindowsTemplateIdsAsync(
        IReadOnlyCollection<int> templateIds,
        CancellationToken cancellationToken) =>
        (await context.ImageTemplates.AsNoTracking()
            .Where(template => templateIds.Contains(template.Id) &&
                               template.OSType == OSType.Windows &&
                               template.ImageType != ImageType.Docker &&
                               template.Status == ImageStatus.Ready)
            .Select(template => template.Id)
            .ToArrayAsync(cancellationToken))
        .ToHashSet();

    public async Task<IReadOnlyDictionary<string, int>> FindReadyDockerImagesAsync(
        IReadOnlyCollection<string> images,
        CancellationToken cancellationToken) =>
        (await context.ImageTemplates.AsNoTracking()
            .Where(template => images.Contains(template.RegistryUrl!) &&
                               template.ImageType == ImageType.Docker &&
                               template.Status == ImageStatus.Ready)
            .OrderBy(template => template.Id)
            .Select(template => new { template.RegistryUrl, template.Id })
            .ToArrayAsync(cancellationToken))
        .GroupBy(template => template.RegistryUrl!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);

    public async Task<IReadOnlyList<OpenChallengeSummaryModel>> ListAsync(
        int gameId,
        int limit,
        int? afterId,
        CancellationToken cancellationToken)
    {
        var query = context.GameChallenges.AsNoTracking()
            .Where(challenge => challenge.GameId == gameId);
        if (afterId.HasValue)
            query = query.Where(challenge => challenge.Id > afterId.Value);
        return await query.OrderBy(challenge => challenge.Id)
            .Take(limit)
            .Select(challenge => new OpenChallengeSummaryModel(
                challenge.Id,
                challenge.Title,
                challenge.Category,
                challenge.Type,
                challenge.IsEnabled,
                challenge.DeadlineUtc,
                challenge.OriginalScore,
                challenge.Environment,
                challenge.ImageTemplateId))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<OpenChallengeModel?> FindAsync(
        int gameId,
        int challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await Query().SingleOrDefaultAsync(
            item => item.GameId == gameId && item.Id == challengeId,
            cancellationToken);
        return challenge is null ? null : Map(challenge);
    }

    public async Task<OpenAwdpServicePageModel> ListAwdpAsync(int gameId, int limit, int? afterId, CancellationToken cancellationToken)
    {
        var query = context.AwdpServices.AsNoTracking().Where(item => item.GameId == gameId);
        if (afterId.HasValue) query = query.Where(item => item.Id > afterId.Value);
        var items = await query.OrderBy(item => item.Id).Take(Math.Clamp(limit, 1, 100) + 1).ToArrayAsync(cancellationToken);
        var page = items.Take(Math.Clamp(limit, 1, 100)).Select(Map).ToArray();
        return new OpenAwdpServicePageModel(page, items.Length > page.Length ? page[^1].Id.ToString() : null);
    }

    public async Task<OpenAwdpServiceModel?> FindAwdpAsync(int gameId, int serviceId, CancellationToken cancellationToken)
    {
        var service = await context.AwdpServices.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.Id == serviceId, cancellationToken);
        return service is null ? null : Map(service);
    }

    private IQueryable<GameChallenge> Query() => context.GameChallenges.AsNoTracking()
        .Include(challenge => challenge.Attachment)
            .ThenInclude(attachment => attachment!.LocalFile)
        .Include(challenge => challenge.Flags)
            .ThenInclude(flag => flag.Attachment)
                .ThenInclude(attachment => attachment!.LocalFile);

    private static OpenChallengeModel Map(GameChallenge challenge) => new(
        challenge.Id,
        challenge.Title,
        challenge.Content,
        challenge.Category,
        challenge.Type,
        challenge.Hints ?? [],
        challenge.IsEnabled,
        challenge.DeadlineUtc,
        challenge.SubmissionLimit,
        challenge.OriginalScore,
        challenge.MinScoreRate,
        challenge.Difficulty,
        challenge.DisableBloodBonus,
        challenge.FlagTemplate,
        challenge.Environment,
        challenge.ContainerImage,
        challenge.ExposePort,
        challenge.ImageTemplateId,
        challenge.CPUCount ?? 1,
        challenge.MemoryLimit ?? 64,
        challenge.StorageLimit ?? 256,
        challenge.NetworkMode ?? NetworkMode.Open,
        challenge.EnableTrafficCapture,
        challenge.FileName,
        challenge.Flags.OrderBy(flag => flag.OrderIndex).ThenBy(flag => flag.Id)
            .Select(flag => new OpenChallengeFlagInfoModel(
                flag.Id,
                flag.Flag,
                flag.OrderIndex,
                flag.Description,
                flag.ScoreMode,
                flag.FixedScore,
                flag.MaxAttempts,
                flag.AttachmentHash,
                flag.AnswerType,
                flag.CustomName,
                MapAttachment(flag.Attachment)))
            .ToArray(),
        MapAttachment(challenge.Attachment));

    private static OpenAwdpServiceModel Map(AwdpService service) => new(
        service.Id, service.Name, service.Content, service.Category, service.Difficulty,
        service.Tags ?? [], service.FlagTemplate, service.ImageName, service.ExposePort,
        service.CheckerScript, service.CheckerEntrypoint, service.ExpScript, service.ExpEntrypoint,
        service.OriginalScore, service.AttackPoints, service.SlaPoints, service.PatchPoints,
        service.ServiceAbnormalPenalty, service.MaxAttackPerRound, service.AttackPhaseMinutes,
        service.PatchPhaseMinutes, service.TotalRounds, service.MaxResetCount, service.MaxRecoveryCount);

    private static OpenChallengeAttachmentInfoModel? MapAttachment(Attachment? attachment)
    {
        var url = attachment?.Url;
        return attachment is null || string.IsNullOrWhiteSpace(url)
            ? null
            : new OpenChallengeAttachmentInfoModel(attachment.Type, url);
    }
}
