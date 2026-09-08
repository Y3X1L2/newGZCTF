using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRemoteAuditService(AppDbContext context, IBlobStorage storage,
    TeamLabAuthorizationService authorization, IDistributedLeaseProvider leases,
    IOptions<TeamLabRemoteAuditOptions> options, TeamLabEventRecorder events, ILogger<TeamLabRemoteAuditService> logger)
{
    private readonly TeamLabRemoteAuditOptions policy = options.Value;
    private const int MaxFileBytes = 16 * 1024;
    private static string ObjectPath(Guid sessionId) => $"teamlab/remote-audit/{sessionId:N}.json";

    public async Task<TeamLabRemoteAuditPage> ListAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken token)
    {
        var session = await RequireAsync(sessionId, actorId, administrator, token);
        var policyExpiry = session.EndedAt?.AddDays(policy.RetentionDays);
        var files = await context.Set<TeamLabRemoteAuditFile>().AsNoTracking()
            .Where(item => item.SessionId == session.Id && item.ReadyAt != null &&
                (policyExpiry == null || policyExpiry > DateTimeOffset.UtcNow) && (item.ExpiresAt == null || item.ExpiresAt > DateTimeOffset.UtcNow))
            .OrderBy(item => item.Id).Select(item => new TeamLabRemoteAuditFileModel(item.Id, item.Size, item.Sha256, item.CreatedAt,
                policyExpiry != null && (item.ExpiresAt == null || policyExpiry < item.ExpiresAt) ? policyExpiry : item.ExpiresAt))
            .ToArrayAsync(token);
        var state = session.EndedAt is null ? "session-active" : session.EndedAt.Value.AddDays(policy.RetentionDays) <= DateTimeOffset.UtcNow
            ? "expired" : files.Length == 0 ? "pending" : "ready";
        return new(state, policy.RetentionDays, files);
    }

    public async Task GenerateAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken token)
    {
        await RequireAsync(sessionId, actorId, administrator, token);
        await using var lease = await leases.AcquireAsync("teamlab:remote-audit", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        var session = await context.TeamLabRemoteSessions.AsNoTracking().Include(item => item.Runtime)
            .SingleAsync(item => item.PublicId == sessionId, linked.Token);
        await GenerateCoreAsync(session, linked.Token);
    }

    private async Task GenerateCoreAsync(TeamLabRemoteSession session, CancellationToken token)
    {
        if (session.EndedAt is null || session.Status is not (TeamLabRemoteSessionStatus.Ended or TeamLabRemoteSessionStatus.Failed))
            throw new TeamLabApiContractException("remote_audit_session_active", "会话结束并完成清理后才可归档证据。", 409);
        var expires = session.EndedAt.Value.AddDays(policy.RetentionDays);
        if (expires <= DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("remote_audit_expired", "会话已超过审计保留期限。", 410);
        var path = ObjectPath(session.PublicId);
        var file = await context.Set<TeamLabRemoteAuditFile>().SingleOrDefaultAsync(item => item.SessionId == session.Id && item.RelativePath == path, token);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, evidenceType = "session-lifecycle", sessionId = session.PublicId,
            runtimeId = session.Runtime.PublicId, session.Generation, assetId = session.RuntimeAssetId,
            operatorId = session.RequestedByUserId, workerNodeId = session.WorkerNodeId,
            protocol = session.Protocol.ToString(), status = session.Status.ToString(), session.Reason,
            session.CreatedAt, session.ConnectedAt, session.EndedAt, session.EndReason,
            session.CorrelationId, contentRecorded = false
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (bytes.Length > MaxFileBytes) throw new InvalidOperationException("Audit evidence exceeds the bounded schema size.");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (file is null)
        {
            var used = await context.Set<TeamLabRemoteAuditFile>().SumAsync(item => (long?)item.Size, token) ?? 0;
            if (used > policy.MaxStorageBytes - bytes.Length)
                throw new TeamLabApiContractException("remote_audit_quota_exceeded", "审计存储配额已满，请等待过期清理或调整配额。", 409);
            file = new TeamLabRemoteAuditFile
            {
                SessionId = session.Id, RelativePath = path, ContentType = "application/json",
                Size = bytes.Length, Sha256 = digest, ExpiresAt = expires
            };
            context.Set<TeamLabRemoteAuditFile>().Add(file);
            await context.SaveChangesAsync(token);
        }
        else if (file.Sha256 != digest || file.Size != bytes.Length)
            throw new TeamLabApiContractException("remote_audit_source_changed", "会话事实与已登记证据不一致，不能覆盖审计证据。", 409);
        if (file.ReadyAt is not null && await storage.ExistsAsync(path, token)) return;
        file.ReadyAt = null;
        await context.SaveChangesAsync(token);
        await storage.EnsureInitializedAsync(token);
        await using var content = new MemoryStream(bytes, writable: false);
        await storage.WriteAsync(path, content, false, token);
        file.ReadyAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
    }

    public async Task<TeamLabRemoteAuditDownload> DownloadAsync(Guid sessionId, long fileId, Guid actorId,
        bool administrator, CancellationToken token)
    {
        var session = await RequireAsync(sessionId, actorId, administrator, token);
        var file = await context.Set<TeamLabRemoteAuditFile>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == fileId && item.SessionId == session.Id, token)
            ?? throw new TeamLabApiContractException("remote_audit_not_found", "未找到审计文件。", 404);
        if (file.ExpiresAt <= DateTimeOffset.UtcNow || session.EndedAt?.AddDays(policy.RetentionDays) <= DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("remote_audit_expired", "审计文件已过期。", 410);
        if (file.ReadyAt is null)
            throw new TeamLabApiContractException("remote_audit_pending", "审计文件尚未完成归档。", 409);
        if (file.RelativePath != ObjectPath(session.PublicId) || file.Size is <= 0 or > MaxFileBytes)
            throw new TeamLabApiContractException("remote_audit_integrity_failed", "审计文件元数据校验失败。", 409);
        if (!await storage.ExistsAsync(file.RelativePath, token))
            throw new TeamLabApiContractException("remote_audit_missing", "审计对象缺失，请检查存储后恢复。", 409);
        await using var source = await storage.OpenReadAsync(file.RelativePath, token);
        var bytes = new byte[(int)file.Size];
        try { await source.ReadExactlyAsync(bytes, token); }
        catch (EndOfStreamException)
        { throw new TeamLabApiContractException("remote_audit_integrity_failed", "审计文件内容不完整。", 409); }
        var extra = new byte[1];
        if (await source.ReadAsync(extra, token) != 0 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != file.Sha256)
            throw new TeamLabApiContractException("remote_audit_integrity_failed", "审计文件内容校验失败。", 409);
        await RequireAsync(sessionId, actorId, administrator, token);
        events.Record(session.Runtime, "remote-audit", TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.RemoteAuditDownloaded, OperationalEventOutcome.Succeeded,
            "下载远程会话操作审计证据", detail: new Dictionary<string, object?>
            { ["actorUserId"] = actorId, ["remoteSessionId"] = sessionId, ["auditFileId"] = fileId });
        await context.SaveChangesAsync(token);
        return new(bytes, $"session-{session.PublicId:N}-audit.json");
    }

    public async Task MaintainAsync(CancellationToken token)
    {
        await using var lease = await leases.AcquireAsync("teamlab:remote-audit", TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(30), token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = linked.Token;
        long after = 0;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-policy.RetentionDays);
        var fileUpperBound = await context.Set<TeamLabRemoteAuditFile>().MaxAsync(item => (long?)item.Id, token) ?? 0;
        while (true)
        {
            var expired = await context.Set<TeamLabRemoteAuditFile>().Include(item => item.Session)
                .Where(item => item.Id > after && item.Id <= fileUpperBound &&
                    (item.ExpiresAt <= DateTimeOffset.UtcNow || item.Session.EndedAt <= cutoff)).OrderBy(item => item.Id).Take(100).ToArrayAsync(token);
            if (expired.Length == 0) break;
            after = expired[^1].Id;
            foreach (var file in expired)
            {
                try
                {
                    if (file.RelativePath != ObjectPath(file.Session.PublicId)) continue;
                    await storage.DeleteAsync(file.RelativePath, token);
                    context.Remove(file);
                    await context.SaveChangesAsync(token);
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    context.Entry(file).State = EntityState.Unchanged;
                    logger.LogWarning("Remote audit cleanup pending for file {FileId}", file.Id);
                }
            }
        }
        after = 0;
        var sessionUpperBound = await context.TeamLabRemoteSessions.MaxAsync(item => (long?)item.Id, token) ?? 0;
        while (true)
        {
            var sessions = await context.TeamLabRemoteSessions.AsNoTracking().Include(item => item.Runtime)
                .Where(item => item.Id > after && item.Id <= sessionUpperBound && item.EndedAt > cutoff &&
                    (item.Status == TeamLabRemoteSessionStatus.Ended || item.Status == TeamLabRemoteSessionStatus.Failed) && !item.AuditFiles.Any(file => file.ReadyAt != null))
                .OrderBy(item => item.Id).Take(100).ToArrayAsync(token);
            if (sessions.Length == 0) break;
            after = sessions[^1].Id;
            foreach (var session in sessions)
            {
                try { await GenerateCoreAsync(session, token); }
                catch (TeamLabApiContractException error) when (error.Code == "remote_audit_quota_exceeded") { return; }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    context.ChangeTracker.Clear();
                    logger.LogWarning("Remote audit archival pending for session {SessionId}", session.PublicId);
                }
            }
        }
    }

    private async Task<TeamLabRemoteSession> RequireAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken token)
    {
        var session = await context.TeamLabRemoteSessions.AsNoTracking().Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.PublicId == sessionId, token)
            ?? throw new TeamLabApiContractException("remote_session_not_found", "未找到远程会话。", 404);
        await authorization.RequirePermissionAsync(session.Runtime.PublicId, actorId, administrator,
            session.RequestedByUserId == actorId ? TeamLabRuntimePermission.RemoteSessionOperate : TeamLabRuntimePermission.MetadataRead, token);
        return session;
    }
}
