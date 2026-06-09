using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Container.Manager;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class AwdpPatchService(
    AppDbContext context,
    IAwdpRepository awdpRepository,
    IContainerPatchApplicator patchApplicator,
    AwdpScriptRunner scriptRunner,
    IGameEventRepository eventRepository,
    CacheHelper cacheHelper,
    IHubContext<MonitorHub, IMonitorClient> hub,
    ILogger<AwdpPatchService> logger)
{
    const long MaxPatchSize = 16 * 1024 * 1024;
    const int MaxPatchEntries = 512;

    public async Task<(AwdpPatchSubmission? Submission, string? Error)> SubmitPatch(int gameId, int teamId,
        int serviceId, IFormFile file, CancellationToken token = default)
    {
        if (file.Length <= 0)
            return (null, "补丁包不能为空");

        if (file.Length > MaxPatchSize)
            return (null, "补丁包大小不能超过 16 MiB");

        if (!IsArchiveName(file.FileName))
            return (null, "补丁包必须是 .tar.gz 或 .tgz 文件");

        var round = await awdpRepository.GetCurrentRound(gameId, token);
        if (round is null)
            return (null, "当前没有进行中的 AWDP 轮次");

        if (round.Status != AwdpRoundStatus.PatchPhase)
            return (null, "当前不是修补阶段");

        var service = await awdpRepository.GetService(serviceId, token);
        if (service is null || service.GameId != gameId)
            return (null, "服务不存在");

        var instance = await awdpRepository.GetInstanceByTeamAndService(teamId, serviceId, token);
        if (instance is null)
            return (null, "服务实例不存在");

        if (instance.Container is null || !instance.IsRunning || instance.Container.Status != ContainerStatus.Running)
            return (null, "服务容器不存在或未运行");

        var roundPatches = await awdpRepository.GetPatchSubmissionsByRound(round.Id, token);
        var resets = await awdpRepository.GetResetRecordsByGame(gameId, token);
        var recoveries = await awdpRepository.GetRecoveryRecordsByGame(gameId, token);
        var latestSubmission = AwdpPatchStateResolver.GetEffectivePatch(service.Id, teamId, roundPatches, resets,
            recoveries, round.StartTime, round.EndTime);
        if (latestSubmission?.FinalStatus == AwdpPatchStatus.ExpFailed)
            return (null, "本轮该服务已通过补丁验证，无需重复提交");

        var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, token);
        buffer.Position = 0;

        var hash = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
        buffer.Position = 0;

        if (!ValidatePatchArchive(buffer, out var validationError))
            return (null, validationError);

        var flag = await awdpRepository.GetFlag(round.Id, service.Id, teamId, token);
        buffer.Position = 0;

        var result = await ApplyAndVerifyPatch(service, instance, flag?.FlagValue ?? string.Empty, buffer, token);

        var submission = new AwdpPatchSubmission
        {
            RoundId = round.Id,
            ServiceId = service.Id,
            TeamId = teamId,
            PatchFileHash = hash,
            SubmittedAt = DateTimeOffset.UtcNow,
            CheckerResult = result.CheckerResult,
            ExpResult = result.ExpResult,
            FinalStatus = result.FinalStatus,
            Message = result.Message
        };

        await context.AwdpPatchSubmissions.AddAsync(submission, token);
        await context.SaveChangesAsync(token);

        var checkerTask = await context.AwdpCheckerTasks
            .FirstOrDefaultAsync(t =>
                t.RoundId == round.Id && t.ServiceId == service.Id && t.TeamId == teamId, token);
        if (checkerTask is null)
        {
            await context.AwdpCheckerTasks.AddAsync(new AwdpCheckerTask
            {
                RoundId = round.Id,
                ServiceId = service.Id,
                TeamId = teamId,
                Status = result.CheckerResult,
                Message = result.Message,
                ExecutedAt = submission.SubmittedAt
            }, token);
        }
        else
        {
            checkerTask.Status = result.CheckerResult;
            checkerTask.Message = result.Message;
            checkerTask.ExecutedAt = submission.SubmittedAt;
        }

        await context.SaveChangesAsync(token);

        submission = await context.AwdpPatchSubmissions
            .Include(p => p.Service)
            .Include(p => p.Team)
            .Include(p => p.Round)
            .SingleAsync(p => p.Id == submission.Id, token);

        await eventRepository.AddEvent(new GameEvent
        {
            GameId = gameId,
            TeamId = teamId,
            Type = EventType.AwdpPatchResult,
            Values = [submission.FinalStatus.ToString(), service.Name, submission.Message ?? string.Empty]
        }, token);

        await hub.Clients.Group($"Game_{gameId}").ReceivedAwdpPatchResult(new AwdpPatchResultModel
        {
            TeamId = teamId,
            TeamName = submission.Team.Name,
            ServiceId = service.Id,
            ServiceName = service.Name,
            Status = submission.FinalStatus,
            Message = submission.Message
        });

        await cacheHelper.FlushScoreboardCache(gameId, token);

        logger.LogInformation(
            "AWDP patch verified: game={GameId}, team={TeamId}, service={ServiceId}, hash={Hash}, status={Status}",
            gameId, teamId, serviceId, hash, submission.FinalStatus);

        return (submission, null);
    }

    async Task<PatchVerificationResult> ApplyAndVerifyPatch(AwdpService service, AwdpServiceInstance instance,
        string flag, Stream archive, CancellationToken token)
    {
        var applyResult = await patchApplicator.ApplyPatchAsync(instance.Container!, archive, token);

        if (!applyResult.IsSupported)
            return new(CheckerStatus.Skipped, AwdpPatchStatus.Unsupported, AwdpPatchStatus.Unsupported,
                applyResult.Message ?? "当前容器后端不支持自动应用补丁");

        if (applyResult.TimedOut)
            return new(CheckerStatus.Skipped, AwdpPatchStatus.Timeout, AwdpPatchStatus.Timeout,
                applyResult.Message ?? "补丁执行超时");

        if (!applyResult.Succeeded)
            return new(CheckerStatus.Skipped, AwdpPatchStatus.CheckerFailed, AwdpPatchStatus.CheckerFailed,
                BuildMessage("补丁应用失败", applyResult.Message));

        var checker = await scriptRunner.RunChecker(service, instance, flag, token);

        if (checker.Status != CheckerStatus.OK)
            return new(checker.Status, AwdpPatchStatus.CheckerFailed, AwdpPatchStatus.CheckerFailed,
                BuildMessage("Checker 未通过", checker.Message));

        var expResult = await scriptRunner.RunExp(service, instance, flag, token);

        return expResult switch
        {
            AwdpPatchStatus.Timeout => new(checker.Status, expResult, AwdpPatchStatus.Timeout, "Exp 执行超时"),
            AwdpPatchStatus.ExpSucceeded => new(checker.Status, expResult, AwdpPatchStatus.ExpSucceeded,
                "Exp 仍然成功，漏洞未修复"),
            AwdpPatchStatus.ExpFailed => new(checker.Status, expResult, AwdpPatchStatus.ExpFailed,
                "补丁验证通过，漏洞已修复"),
            _ => new(checker.Status, expResult, expResult, expResult.ToString())
        };
    }

    static bool IsArchiveName(string fileName) =>
        fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    static bool ValidatePatchArchive(Stream stream, out string error)
    {
        try
        {
            stream.Position = 0;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new TarReader(gzip, leaveOpen: false);

            TarEntry? entry;
            var hasUpdateScript = false;
            var entryCount = 0;

            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                if (++entryCount > MaxPatchEntries)
                {
                    error = $"补丁包文件数量不能超过 {MaxPatchEntries} 个";
                    return false;
                }

                var rawName = entry.Name.Replace('\\', '/');
                var normalized = rawName.TrimStart('/');

                if (rawName.StartsWith('/') || normalized.Split('/').Any(part => part == ".."))
                {
                    error = "补丁包不能包含绝对路径或上级目录路径";
                    return false;
                }

                if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                {
                    error = "补丁包不能包含符号链接或硬链接";
                    return false;
                }

                if (entry.EntryType is TarEntryType.BlockDevice or TarEntryType.CharacterDevice or TarEntryType.Fifo)
                {
                    error = "补丁包不能包含设备文件或 FIFO";
                    return false;
                }

                if (string.Equals(normalized, "update.sh", StringComparison.Ordinal))
                    hasUpdateScript = true;
            }

            if (hasUpdateScript)
            {
                error = string.Empty;
                return true;
            }

            error = "补丁包必须包含 update.sh";
            return false;
        }
        catch (InvalidDataException)
        {
            error = "补丁包不是有效的 tar.gz 归档";
            return false;
        }
        catch (IOException)
        {
            error = "补丁包读取失败";
            return false;
        }
    }

    static string BuildMessage(string prefix, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix}: {detail}";

    readonly record struct PatchVerificationResult(
        CheckerStatus CheckerResult,
        AwdpPatchStatus ExpResult,
        AwdpPatchStatus FinalStatus,
        string Message);
}
