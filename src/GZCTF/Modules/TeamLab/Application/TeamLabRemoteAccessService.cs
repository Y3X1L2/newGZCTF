using GZCTF.Models;
using System.Security.Cryptography;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using System.Net.WebSockets;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRemoteAccessService(
    AppDbContext context,
    TeamLabRemoteAccessAuthorizationService authorization,
    ITeamLabRemoteRelayGateway relays,
    ImageRemoteAccessService imageRemoteAccess,
    GuacamoleRemoteSessionService guacamole,
    TeamLabEventRecorder events,
    IMemoryCache cache,
    ILogger<TeamLabRemoteAccessService> logger) : ITeamLabRemoteAccessService
{
    private const int SessionMinutes = 30;
    private const int MaxActiveSessionsPerOperator = 5;
    private const int MaxActiveSessionsPerNode = 100;

    public async Task<TeamLabRemoteAccessAvailabilityModel> GetAvailabilityAsync(
        Guid runtimeId, int assetId, Guid actorId, bool administrator, CancellationToken cancellationToken)
    {
        await authorization.RequireAsync(runtimeId, actorId, administrator,
            TeamLabOperatorPermission.ViewAssets, cancellationToken);
        var asset = await FindAssetAsync(runtimeId, assetId, cancellationToken);
        return await AvailabilityAsync(asset, cancellationToken);
    }

    /// <summary>
    /// Bounded batch projection of per-asset remote availability. A single unavailable
    /// asset never fails the whole batch; each entry carries its own availability state.
    /// </summary>
    public async Task<IReadOnlyList<TeamLabRemoteAccessAvailabilityModel>> GetAvailabilityBatchAsync(
        Guid runtimeId, Guid actorId, bool administrator, CancellationToken cancellationToken)
    {
        await authorization.RequireAsync(runtimeId, actorId, administrator,
            TeamLabOperatorPermission.ViewAssets, cancellationToken);
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => (int?)item.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        var assets = await context.TeamLabRuntimeAssets.AsNoTracking()
            .Where(item => item.RuntimeId == runtime)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var templateIds = assets.Where(item => item.Kind == TeamLabResourceKind.Vm && item.SourceTemplateId is not null)
            .Select(item => item.SourceTemplateId!.Value).Distinct().ToArray();
        var configurations = templateIds.Length == 0
            ? new Dictionary<int, ImageTemplateRemoteAccess>()
            : await context.ImageTemplateRemoteAccesses.AsNoTracking()
                .Where(item => templateIds.Contains(item.ImageTemplateId))
                .ToDictionaryAsync(item => item.ImageTemplateId, cancellationToken);
        return assets.Select(asset => Availability(asset,
            asset.SourceTemplateId is { } templateId ? configurations.GetValueOrDefault(templateId) : null)).ToArray();
    }

    public async Task<TeamLabRemoteSessionModel> CreateAsync(
        Guid runtimeId, int assetId, Guid actorId, bool administrator, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 4 or > 500)
            throw new TeamLabApiContractException("remote_access_reason_invalid", "访问原因需为 4-500 个字符", 422);
        await authorization.RequireAsync(runtimeId, actorId, administrator,
            TeamLabOperatorPermission.OperateAssets, cancellationToken);
        var asset = await FindAssetAsync(runtimeId, assetId, cancellationToken);
        var availability = await AvailabilityAsync(asset, cancellationToken);
        if (!availability.Available || availability.Protocol is null)
            throw new TeamLabApiContractException("remote_access_unavailable", availability.UnavailableReason ?? "远程访问当前不可用", 409);
        if (asset.WorkerNodeId is null || string.IsNullOrWhiteSpace(asset.RuntimeResourceId) ||
            (asset.Kind == TeamLabResourceKind.Vm && (string.IsNullOrWhiteSpace(asset.NativeIdentity) || string.IsNullOrWhiteSpace(asset.IpAddress))))
            throw new TeamLabApiContractException("remote_access_asset_unresolved", "运行时资源缺少稳定的节点、资源标识或地址", 409);

        var runtime = await context.TeamLabRuntimes.SingleAsync(item => item.PublicId == runtimeId, cancellationToken);
        var session = new TeamLabRemoteSession
        {
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            RuntimeAssetId = asset.Id,
            WorkerNodeId = asset.WorkerNodeId.Value,
            RequestedByUserId = actorId,
            Protocol = availability.Protocol.Value,
            Reason = reason.Trim(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(SessionMinutes)
        };
        await ReserveSessionAsync(session, asset, cancellationToken);

        try
        {
            if (session.Protocol == TeamLabRemoteProtocol.ContainerTerminal)
            {
                await ActivateSessionAsync(session, runtime, cancellationToken);
                events.Record(runtime, "remote-access", TeamLabEventLevel.Success,
                    OperationalEventCodes.TeamLab.RemoteSessionCreated, OperationalEventOutcome.Succeeded,
                    $"已为 {asset.Name} 创建容器远程终端会话", workerNodeId: session.WorkerNodeId,
                    detail: RemoteDetail(session, asset, actorId));
                await context.SaveChangesAsync(cancellationToken);
                return ToModel(session, asset.Name, runtimeId);
            }
            var configuration = await context.ImageTemplateRemoteAccesses.AsNoTracking()
                .SingleOrDefaultAsync(item => item.ImageTemplateId == asset.SourceTemplateId, cancellationToken)
                ?? throw new TeamLabApiContractException("remote_access_configuration_missing", "该镜像未配置远程账号", 409);
            var relay = await relays.CreateAsync(session.WorkerNodeId, new TeamLabRemoteRelayRequest(
                session.PublicId, runtime.Id, runtime.Generation, asset.RuntimeResourceId!, asset.NativeIdentity!,
                asset.IpAddress!, configuration.Port, session.ExpiresAt), cancellationToken);
            session.RelayId = relay.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (session.Protocol is TeamLabRemoteProtocol.Rdp or TeamLabRemoteProtocol.Ssh)
            {
                if (string.IsNullOrWhiteSpace(configuration.Username) || string.IsNullOrWhiteSpace(configuration.ProtectedSecret))
                    throw new TeamLabApiContractException("remote_access_credential_unavailable", "该镜像尚未配置完整的静态运维账号。", 409);
                var node = await context.WorkerNodes.AsNoTracking().SingleAsync(item => item.Id == session.WorkerNodeId, cancellationToken);
                var username = configuration.Username;
                string secret;
                try
                {
                    secret = imageRemoteAccess.RevealSecret(configuration);
                }
                catch (Exception exception) when (exception is CryptographicException or InvalidOperationException)
                {
                    throw new TeamLabApiContractException("remote_access_credential_invalid",
                        "该镜像的运维凭据无法解密，请在镜像模板中重新设置。", 409);
                }
                var guacamoleSession = session.Protocol == TeamLabRemoteProtocol.Rdp
                    ? await guacamole.CreateRdpAsync(session.PublicId, node.HostAddress, relay.Port, username, secret, cancellationToken)
                    : await guacamole.CreateSshAsync(session.PublicId, node.HostAddress, relay.Port, username, secret, cancellationToken);
                session.GuacamoleConnectionId = guacamoleSession.ConnectionId;
                session.GuacamoleUserId = guacamoleSession.UserId;
                cache.Set(ConnectUrlKey(session.PublicId), guacamoleSession.ConnectUrl, TimeSpan.FromMinutes(5));
            }
            await ActivateSessionAsync(session, runtime, cancellationToken);
            events.Record(runtime, "remote-access", TeamLabEventLevel.Success,
                OperationalEventCodes.TeamLab.RemoteSessionCreated, OperationalEventOutcome.Succeeded,
                $"已为 {asset.Name} 创建 {session.Protocol} 远程会话", workerNodeId: session.WorkerNodeId,
                detail: RemoteDetail(session, asset, actorId));
            await context.SaveChangesAsync(cancellationToken);
            return ToModel(session, asset.Name, runtimeId);
        }
        catch
        {
            await MarkFailedCreationAsync(session, CancellationToken.None);
            try { await guacamole.DeleteAsync(session.GuacamoleConnectionId, session.GuacamoleUserId, CancellationToken.None); }
            catch { }
            try { await relays.DeleteAsync(session.WorkerNodeId, session.PublicId, CancellationToken.None); }
            catch { }
            throw;
        }
    }

    public async Task<TeamLabRemoteSessionModel> GetAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken cancellationToken)
    {
        var session = await context.TeamLabRemoteSessions.AsNoTracking()
            .Include(item => item.RuntimeAsset).Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.PublicId == sessionId, cancellationToken)
            ?? throw new TeamLabApiContractException("remote_session_not_found", "未找到远程访问会话", 404);
        var permission = session.RequestedByUserId == actorId ? TeamLabOperatorPermission.ViewAssets : TeamLabOperatorPermission.OperateAssets;
        await authorization.RequireAsync(session.Runtime.PublicId, actorId, administrator, permission, cancellationToken);
        return ToModel(session, session.RuntimeAsset.Name, session.Runtime.PublicId);
    }

    public async Task<TeamLabRemoteConnectModel> ConnectAsync(
        Guid sessionId, Guid actorId, bool administrator, CancellationToken cancellationToken)
    {
        var session = await context.TeamLabRemoteSessions.Include(item => item.Runtime).Include(item => item.RuntimeAsset)
            .SingleOrDefaultAsync(item => item.PublicId == sessionId, cancellationToken)
            ?? throw new TeamLabApiContractException("remote_session_not_found", "未找到远程访问会话", 404);
        await authorization.RequireAsync(session.Runtime.PublicId, actorId, administrator,
            TeamLabOperatorPermission.OperateAssets, cancellationToken);
        if (session.Protocol == TeamLabRemoteProtocol.ContainerTerminal)
            throw new TeamLabApiContractException("remote_session_terminal", "该会话请使用终端接口访问", 409);
        if (!cache.TryGetValue<string>(ConnectUrlKey(sessionId), out var url) || string.IsNullOrWhiteSpace(url))
            throw new TeamLabApiContractException("remote_session_connect_expired", "一次性远程连接链接已过期，请创建新会话", 409);
        if (!await ConnectSessionAsync(session, cancellationToken))
            throw new TeamLabApiContractException("remote_session_unavailable", "远程访问会话当前不可用", 409);
        cache.Remove(ConnectUrlKey(sessionId));
        events.Record(session.Runtime, "remote-access", TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.RemoteSessionConnected, OperationalEventOutcome.Succeeded,
            $"已为 {session.RuntimeAsset.Name} 建立 {session.Protocol} 远程会话连接", workerNodeId: session.WorkerNodeId,
            detail: RemoteDetail(session, session.RuntimeAsset, actorId));
        await context.SaveChangesAsync(cancellationToken);
        return new TeamLabRemoteConnectModel(url, session.ExpiresAt);
    }

    public async Task ProxyTerminalAsync(Guid sessionId, Guid actorId, bool administrator, WebSocket socket, CancellationToken cancellationToken)
    {
        var session = await context.TeamLabRemoteSessions.Include(item => item.Runtime).Include(item => item.RuntimeAsset)
            .SingleOrDefaultAsync(item => item.PublicId == sessionId, cancellationToken)
            ?? throw new TeamLabApiContractException("remote_session_not_found", "未找到远程访问会话", 404);
        await authorization.RequireAsync(session.Runtime.PublicId, actorId, administrator, TeamLabOperatorPermission.OperateAssets, cancellationToken);
        if (session.Protocol != TeamLabRemoteProtocol.ContainerTerminal)
            throw new TeamLabApiContractException("remote_session_unavailable", "终端会话当前不可用", 409);
        if (!await ConnectSessionAsync(session, cancellationToken))
            throw new TeamLabApiContractException("remote_session_unavailable", "终端会话当前不可用", 409);
        try
        {
            await relays.ProxyTerminalAsync(session.WorkerNodeId, session.PublicId, session.RuntimeId, session.Generation,
                session.RuntimeAsset.RuntimeResourceId!, session.ExpiresAt, socket, cancellationToken);
        }
        finally
        {
            await EndAsync(sessionId, actorId, administrator, "terminal_disconnected", CancellationToken.None);
        }
    }

    public async Task EndAsync(Guid sessionId, Guid actorId, bool administrator, string reason, CancellationToken cancellationToken)
    {
        var session = await context.TeamLabRemoteSessions.Include(item => item.Runtime).Include(item => item.RuntimeAsset)
            .SingleOrDefaultAsync(item => item.PublicId == sessionId, cancellationToken)
            ?? throw new TeamLabApiContractException("remote_session_not_found", "未找到远程访问会话", 404);
        var permission = session.RequestedByUserId == actorId ? TeamLabOperatorPermission.ViewAssets : TeamLabOperatorPermission.OperateAssets;
        await authorization.RequireAsync(session.Runtime.PublicId, actorId, administrator, permission, cancellationToken);
        if (session.Status is TeamLabRemoteSessionStatus.Ended or TeamLabRemoteSessionStatus.Failed) return;
        session.Status = TeamLabRemoteSessionStatus.Ending;
        await context.SaveChangesAsync(cancellationToken);
        if (!await CompleteEndingAsync(session, reason, actorId, cancellationToken))
            throw new TeamLabApiContractException("remote_session_cleanup_pending", "远程会话正在等待基础设施清理完成，请稍后刷新状态。", 409);
    }

    public async Task ExpireAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await context.TeamLabRemoteSessions
            .Where(item => item.Status == TeamLabRemoteSessionStatus.Ending ||
                           item.ExpiresAt <= now &&
                           (item.Status == TeamLabRemoteSessionStatus.Creating ||
                            item.Status == TeamLabRemoteSessionStatus.Ready ||
                            item.Status == TeamLabRemoteSessionStatus.Connected))
            .Take(100)
            .ToArrayAsync(cancellationToken);
        foreach (var session in sessions) session.Status = TeamLabRemoteSessionStatus.Ending;
        if (sessions.Length > 0) await context.SaveChangesAsync(cancellationToken);
        foreach (var session in sessions)
        {
            try { await CompleteEndingAsync(session, "expired", Guid.Empty, cancellationToken); }
            catch { }
        }
        if (sessions.Length > 0) await context.SaveChangesAsync(cancellationToken);
    }

    public async Task EndRuntimeSessionsAsync(int runtimeId, int generation, string reason, CancellationToken cancellationToken)
    {
        var sessions = await context.TeamLabRemoteSessions
            .Where(item => item.RuntimeId == runtimeId && item.Generation == generation &&
                           (item.Status == TeamLabRemoteSessionStatus.Creating ||
                            item.Status == TeamLabRemoteSessionStatus.Ready ||
                            item.Status == TeamLabRemoteSessionStatus.Connected ||
                            item.Status == TeamLabRemoteSessionStatus.Ending))
            .ToArrayAsync(cancellationToken);
        foreach (var session in sessions) session.Status = TeamLabRemoteSessionStatus.Ending;
        if (sessions.Length > 0) await context.SaveChangesAsync(cancellationToken);
        foreach (var session in sessions)
        {
            try { await CompleteEndingAsync(session, reason, Guid.Empty, cancellationToken); } catch { }
        }
        if (sessions.Length > 0) await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<TeamLabRuntimeAsset> FindAssetAsync(Guid runtimeId, int assetId, CancellationToken cancellationToken) =>
        await context.TeamLabRuntimeAssets.Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.Id == assetId && item.Runtime.PublicId == runtimeId, cancellationToken)
        ?? throw new TeamLabApiContractException("runtime_asset_not_found", "未找到运行时资源", 404);

    private async Task ReserveSessionAsync(
        TeamLabRemoteSession session,
        TeamLabRuntimeAsset asset,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await LockSessionCapacityAsync(TeamLabRuntimeCleanupService.RuntimeLockKey(session.RuntimeId), cancellationToken);
        await LockSessionCapacityAsync(OperatorLockKey(session.RequestedByUserId), cancellationToken);
        await LockSessionCapacityAsync(NodeLockKey(session.WorkerNodeId), cancellationToken);

        await context.Entry(asset.Runtime).ReloadAsync(cancellationToken);
        if (asset.Runtime.Status != TeamLabRuntimeStatus.Running)
            throw new TeamLabApiContractException("remote_access_runtime_unavailable", "运行时当前不允许建立运维会话", 409);
        await RequireSessionCapacityAsync(asset, session.Protocol, session.RequestedByUserId, cancellationToken);
        context.TeamLabRemoteSessions.Add(session);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new TeamLabApiContractException(
                "remote_access_session_exists",
                "该资源已存在活跃的远程会话",
                409);
        }
    }

    private Task LockSessionCapacityAsync(long lockKey, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", [lockKey], cancellationToken);

    private async Task RequireSessionCapacityAsync(TeamLabRuntimeAsset asset, TeamLabRemoteProtocol protocol,
        Guid actorId, CancellationToken cancellationToken)
    {
        var active = new[] { TeamLabRemoteSessionStatus.Creating, TeamLabRemoteSessionStatus.Ready,
            TeamLabRemoteSessionStatus.Connected, TeamLabRemoteSessionStatus.Ending };
        var sessions = context.TeamLabRemoteSessions.AsNoTracking().Where(item => active.Contains(item.Status));
        if (await sessions.AnyAsync(item => item.RequestedByUserId == actorId && item.RuntimeAssetId == asset.Id &&
                                            item.Protocol == protocol, cancellationToken))
            throw new TeamLabApiContractException("remote_access_session_exists", "该资源已存在活跃的远程会话", 409);
        if (await sessions.CountAsync(item => item.RequestedByUserId == actorId, cancellationToken) >= MaxActiveSessionsPerOperator)
            throw new TeamLabApiContractException("remote_access_operator_limit", "操作者已达活跃远程会话数上限", 429);
        if (await sessions.CountAsync(item => item.WorkerNodeId == asset.WorkerNodeId, cancellationToken) >= MaxActiveSessionsPerNode)
            throw new TeamLabApiContractException("remote_access_node_limit", "目标节点已达远程访问会话数上限", 429);
    }

    private static long OperatorLockKey(Guid actorId)
    {
        var bytes = actorId.ToByteArray();
        return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8) ^ 0x544C524100000000L;
    }

    private static long NodeLockKey(Guid workerNodeId)
    {
        var bytes = workerNodeId.ToByteArray();
        return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8) ^ 0x544C524E00000000L;
    }

    private async Task ActivateSessionAsync(
        TeamLabRemoteSession session,
        TeamLabRuntime runtime,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await LockSessionCapacityAsync(TeamLabRuntimeCleanupService.RuntimeLockKey(runtime.Id), cancellationToken);
        await context.Entry(session).ReloadAsync(cancellationToken);
        await context.Entry(runtime).ReloadAsync(cancellationToken);
        if (session.Status != TeamLabRemoteSessionStatus.Creating || runtime.Status != TeamLabRuntimeStatus.Running)
            throw new TeamLabApiContractException(
                "remote_access_runtime_unavailable",
                "运行时正在停止或已停止，无法建立运维会话",
                409);
        session.Status = TeamLabRemoteSessionStatus.Ready;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task MarkFailedCreationAsync(TeamLabRemoteSession session, CancellationToken cancellationToken)
    {
        await context.Entry(session).ReloadAsync(cancellationToken);
        if (session.Status != TeamLabRemoteSessionStatus.Creating)
            return;
        session.Status = TeamLabRemoteSessionStatus.Failed;
        session.EndedAt = DateTimeOffset.UtcNow;
        session.EndReason = "relay_create_failed";
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ConnectSessionAsync(TeamLabRemoteSession session, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await LockSessionCapacityAsync(TeamLabRuntimeCleanupService.RuntimeLockKey(session.RuntimeId), cancellationToken);
        await context.Entry(session).ReloadAsync(cancellationToken);
        await context.Entry(session.Runtime).ReloadAsync(cancellationToken);
        if (session.Status != TeamLabRemoteSessionStatus.Ready || session.ExpiresAt <= DateTimeOffset.UtcNow ||
            session.Runtime.Status != TeamLabRuntimeStatus.Running)
            return false;
        session.ConnectedAt ??= DateTimeOffset.UtcNow;
        session.Status = TeamLabRemoteSessionStatus.Connected;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> CompleteEndingAsync(TeamLabRemoteSession session, string reason, Guid actorId, CancellationToken cancellationToken)
    {
        var cleanupFailed = false;
        try
        {
            if (session.Protocol == TeamLabRemoteProtocol.ContainerTerminal)
                await relays.CancelTerminalAsync(session.WorkerNodeId, session.PublicId, cancellationToken);
            else
                await relays.DeleteAsync(session.WorkerNodeId, session.PublicId, cancellationToken);
        }
        catch (Exception exception)
        {
            cleanupFailed = true;
            logger.LogWarning(exception, "移除远程会话 {SessionId} 的 TeamLab 中继失败", session.PublicId);
        }
        try { await guacamole.DeleteAsync(session.GuacamoleConnectionId, session.GuacamoleUserId, cancellationToken); }
        catch (Exception exception)
        {
            cleanupFailed = true;
            logger.LogWarning(exception, "移除远程会话 {SessionId} 的 Guacamole 资源失败", session.PublicId);
        }
        cache.Remove(ConnectUrlKey(session.PublicId));
        if (cleanupFailed)
        {
            session.Status = TeamLabRemoteSessionStatus.Ending;
            session.EndedAt = null;
            session.EndReason = "cleanup_pending";
            events.Record(session.Runtime, "remote-access", TeamLabEventLevel.Warning,
                OperationalEventCodes.TeamLab.RemoteSessionEnded, OperationalEventOutcome.Failed,
                $"{session.RuntimeAsset.Name} 的 {session.Protocol} 远程会话清理未完成，系统将继续重试",
                workerNodeId: session.WorkerNodeId, detail: RemoteDetail(session, session.RuntimeAsset, actorId));
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }
        session.Status = TeamLabRemoteSessionStatus.Ended;
        session.EndedAt = DateTimeOffset.UtcNow;
        session.EndReason = string.IsNullOrWhiteSpace(reason) ? "closed" : reason[..Math.Min(256, reason.Length)];
        events.Record(session.Runtime, "remote-access", TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.RemoteSessionEnded, OperationalEventOutcome.Succeeded,
            $"已结束 {session.RuntimeAsset.Name} 的 {session.Protocol} 远程会话", workerNodeId: session.WorkerNodeId,
            detail: RemoteDetail(session, session.RuntimeAsset, actorId));
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<TeamLabRemoteAccessAvailabilityModel> AvailabilityAsync(TeamLabRuntimeAsset asset, CancellationToken cancellationToken)
    {
        var configuration = await context.ImageTemplateRemoteAccesses.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ImageTemplateId == asset.SourceTemplateId, cancellationToken);
        return Availability(asset, configuration);
    }

    private static TeamLabRemoteAccessAvailabilityModel Availability(
        TeamLabRuntimeAsset asset,
        ImageTemplateRemoteAccess? configuration)
    {
        if (asset.Status != TeamLabRuntimeStatus.Running) return new(asset.Id, asset.Name, null, false, "资源未在运行");
        if (asset.Kind == TeamLabResourceKind.Docker) return new(asset.Id, asset.Name, TeamLabRemoteProtocol.ContainerTerminal, true, null);
        if (asset.Kind != TeamLabResourceKind.Vm || asset.SourceTemplateId is null)
            return new(asset.Id, asset.Name, null, false, "该资源不支持远程操作");
        if (configuration is null || !configuration.Enabled)
            return new(asset.Id, asset.Name, null, false, "该镜像未配置远程操作");
        if (string.IsNullOrWhiteSpace(configuration.Username) || string.IsNullOrWhiteSpace(configuration.ProtectedSecret))
            return new(asset.Id, asset.Name, null, false, "镜像尚未配置完整的静态运维账号。");
        return new(asset.Id, asset.Name, configuration.Protocol, true, null);
    }

    private static TeamLabRemoteSessionModel ToModel(TeamLabRemoteSession session, string name, Guid runtimeId) => new(
        session.PublicId, runtimeId, session.RuntimeAssetId, name, session.Protocol, session.Status,
        session.Reason, session.CreatedAt, session.ExpiresAt, session.ConnectedAt, session.EndedAt, session.EndReason);

    private static string ConnectUrlKey(Guid sessionId) => "teamlab:remote-connect:" + sessionId.ToString("N");

    private static IReadOnlyDictionary<string, object?> RemoteDetail(TeamLabRemoteSession session, TeamLabRuntimeAsset asset, Guid actorId) =>
        new Dictionary<string, object?>
        {
            ["remoteSessionId"] = session.PublicId,
            ["assetId"] = asset.Id,
            ["assetKey"] = asset.TopologyKey,
            ["protocol"] = session.Protocol.ToString(),
            ["actorUserId"] = actorId,
            ["generation"] = session.Generation
        };
}
