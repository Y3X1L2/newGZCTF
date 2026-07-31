using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.Services;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Net.WebSockets;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRemoteAccessService(
    AppDbContext context,
    TeamLabRemoteAccessAuthorizationService authorization,
    AgentClient agents,
    ImageRemoteAccessService imageRemoteAccess,
    TeamLabRemoteCredentialService credentials,
    GuacamoleRemoteSessionService guacamole,
    TeamLabEventRecorder events,
    IMemoryCache cache) : ITeamLabRemoteAccessService
{
    private const int SessionMinutes = 30;

    public async Task<TeamLabRemoteAccessAvailabilityModel> GetAvailabilityAsync(
        Guid runtimeId, int assetId, Guid actorId, bool administrator, CancellationToken cancellationToken)
    {
        await authorization.RequireAsync(runtimeId, actorId, administrator,
            TeamLabOperatorPermission.ViewAssets, cancellationToken);
        var asset = await FindAssetAsync(runtimeId, assetId, cancellationToken);
        return await AvailabilityAsync(asset, cancellationToken);
    }

    public async Task<TeamLabRemoteSessionModel> CreateAsync(
        Guid runtimeId, int assetId, Guid actorId, bool administrator, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 4 or > 500)
            throw new TeamLabApiContractException("remote_access_reason_invalid", "A 4-500 character access reason is required.", 422);
        await authorization.RequireAsync(runtimeId, actorId, administrator,
            TeamLabOperatorPermission.OperateAssets, cancellationToken);
        var asset = await FindAssetAsync(runtimeId, assetId, cancellationToken);
        var availability = await AvailabilityAsync(asset, cancellationToken);
        if (!availability.Available || availability.Protocol is null)
            throw new TeamLabApiContractException("remote_access_unavailable", availability.UnavailableReason ?? "Remote access is unavailable.", 409);
        if (asset.WorkerNodeId is null || string.IsNullOrWhiteSpace(asset.RuntimeResourceId) ||
            (asset.Kind == TeamLabResourceKind.Vm && (string.IsNullOrWhiteSpace(asset.NativeIdentity) || string.IsNullOrWhiteSpace(asset.IpAddress))))
            throw new TeamLabApiContractException("remote_access_asset_unresolved", "The runtime asset has no stable node, resource identity, or address.", 409);

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
        context.TeamLabRemoteSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            if (session.Protocol == TeamLabRemoteProtocol.ContainerTerminal)
            {
                session.Status = TeamLabRemoteSessionStatus.Ready;
                events.Record(runtime, "remote-access", TeamLabEventLevel.Success,
                    OperationalEventCodes.TeamLab.RemoteSessionCreated, OperationalEventOutcome.Succeeded,
                    $"Container remote terminal session created for {asset.Name}.", workerNodeId: session.WorkerNodeId,
                    detail: RemoteDetail(session, asset, actorId));
                await context.SaveChangesAsync(cancellationToken);
                return ToModel(session, asset.Name, runtimeId);
            }
            var configuration = await context.ImageTemplateRemoteAccesses.AsNoTracking()
                .SingleOrDefaultAsync(item => item.ImageTemplateId == asset.SourceTemplateId, cancellationToken)
                ?? throw new TeamLabApiContractException("remote_access_configuration_missing", "The image has no configured remote account.", 409);
            var relay = await agents.CreateRemoteRelayAsync(session.WorkerNodeId, new AgentRemoteRelayRequest(
                session.PublicId, runtime.Id, runtime.Generation, asset.RuntimeResourceId!, asset.NativeIdentity!,
                asset.IpAddress!, configuration.Port, session.ExpiresAt), cancellationToken);
            session.RelayId = relay.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (session.Protocol is TeamLabRemoteProtocol.Rdp or TeamLabRemoteProtocol.Ssh)
            {
                var credential = configuration.CredentialMode == RemoteCredentialMode.PlatformGenerated
                    ? await context.TeamLabRuntimeRemoteCredentials.SingleOrDefaultAsync(item =>
                        item.RuntimeId == runtime.Id && item.Generation == runtime.Generation && item.RuntimeAssetId == asset.Id &&
                        item.Protocol == session.Protocol && item.RevokedAt == null, cancellationToken)
                    : null;
                if (configuration.CredentialMode == RemoteCredentialMode.ExistingAccount && string.IsNullOrWhiteSpace(configuration.Username))
                    throw new TeamLabApiContractException("remote_access_credential_unavailable", "The image does not yet provide a remote account for operations.", 409);
                if (configuration.CredentialMode == RemoteCredentialMode.PlatformGenerated && credential is null)
                    throw new TeamLabApiContractException("remote_access_credential_unavailable", "The platform remote account is not ready.", 409);
                var node = await context.WorkerNodes.AsNoTracking().SingleAsync(item => item.Id == session.WorkerNodeId, cancellationToken);
                var username = credential?.Username ?? configuration.Username!;
                var secret = credential is null ? imageRemoteAccess.RevealSecret(configuration) : credentials.RevealSecret(credential);
                var guacamoleSession = session.Protocol == TeamLabRemoteProtocol.Rdp
                    ? await guacamole.CreateRdpAsync(session.PublicId, node.HostAddress, relay.Port, username, secret, cancellationToken)
                    : await guacamole.CreateSshAsync(session.PublicId, node.HostAddress, relay.Port, username, secret, cancellationToken);
                session.GuacamoleConnectionId = guacamoleSession.ConnectionId;
                session.GuacamoleUserId = guacamoleSession.UserId;
                cache.Set(ConnectUrlKey(session.PublicId), guacamoleSession.ConnectUrl, TimeSpan.FromMinutes(5));
            }
            session.Status = TeamLabRemoteSessionStatus.Ready;
            events.Record(runtime, "remote-access", TeamLabEventLevel.Success,
                OperationalEventCodes.TeamLab.RemoteSessionCreated, OperationalEventOutcome.Succeeded,
                $"Remote {session.Protocol} session created for {asset.Name}.", workerNodeId: session.WorkerNodeId,
                detail: RemoteDetail(session, asset, actorId));
            await context.SaveChangesAsync(cancellationToken);
            return ToModel(session, asset.Name, runtimeId);
        }
        catch
        {
            session.Status = TeamLabRemoteSessionStatus.Failed;
            session.EndedAt = DateTimeOffset.UtcNow;
            session.EndReason = "relay_create_failed";
            await context.SaveChangesAsync(CancellationToken.None);
            try { await agents.DeleteRemoteRelayAsync(session.WorkerNodeId, session.PublicId, CancellationToken.None); }
            catch { }
            throw;
        }
    }

    public async Task<TeamLabRemoteSessionModel> GetAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken cancellationToken)
    {
        var session = await context.TeamLabRemoteSessions.AsNoTracking()
            .Include(item => item.RuntimeAsset).Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.PublicId == sessionId, cancellationToken)
            ?? throw new TeamLabApiContractException("remote_session_not_found", "The remote access session was not found.", 404);
        var permission = session.RequestedByUserId == actorId ? TeamLabOperatorPermission.ViewAssets : TeamLabOperatorPermission.OperateAssets;
        await authorization.RequireAsync(session.Runtime.PublicId, actorId, administrator, permission, cancellationToken);
        return ToModel(session, session.RuntimeAsset.Name, session.Runtime.PublicId);
    }

    public async Task<TeamLabRemoteConnectModel> ConnectAsync(
        Guid sessionId, Guid actorId, bool administrator, CancellationToken cancellationToken)
    {
        var session = await context.TeamLabRemoteSessions.Include(item => item.Runtime).Include(item => item.RuntimeAsset)
            .SingleOrDefaultAsync(item => item.PublicId == sessionId, cancellationToken)
            ?? throw new TeamLabApiContractException("remote_session_not_found", "The remote access session was not found.", 404);
        await authorization.RequireAsync(session.Runtime.PublicId, actorId, administrator,
            TeamLabOperatorPermission.OperateAssets, cancellationToken);
        if (session.Status != TeamLabRemoteSessionStatus.Ready || session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("remote_session_unavailable", "The remote access session is not available.", 409);
        if (session.Protocol == TeamLabRemoteProtocol.ContainerTerminal)
            throw new TeamLabApiContractException("remote_session_terminal", "Use the terminal endpoint for this session.", 409);
        if (!cache.TryGetValue<string>(ConnectUrlKey(sessionId), out var url) || string.IsNullOrWhiteSpace(url))
            throw new TeamLabApiContractException("remote_session_connect_expired", "The one-time remote connection link has expired. Create a new session.", 409);
        cache.Remove(ConnectUrlKey(sessionId));
        session.ConnectedAt ??= DateTimeOffset.UtcNow;
        session.Status = TeamLabRemoteSessionStatus.Connected;
        events.Record(session.Runtime, "remote-access", TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.RemoteSessionConnected, OperationalEventOutcome.Succeeded,
            $"Remote {session.Protocol} session connected for {session.RuntimeAsset.Name}.", workerNodeId: session.WorkerNodeId,
            detail: RemoteDetail(session, session.RuntimeAsset, actorId));
        await context.SaveChangesAsync(cancellationToken);
        return new TeamLabRemoteConnectModel(url, session.ExpiresAt);
    }

    public async Task ProxyTerminalAsync(Guid sessionId, Guid actorId, bool administrator, WebSocket socket, CancellationToken cancellationToken)
    {
        var session = await context.TeamLabRemoteSessions.Include(item => item.Runtime).Include(item => item.RuntimeAsset)
            .SingleOrDefaultAsync(item => item.PublicId == sessionId, cancellationToken)
            ?? throw new TeamLabApiContractException("remote_session_not_found", "The remote access session was not found.", 404);
        await authorization.RequireAsync(session.Runtime.PublicId, actorId, administrator, TeamLabOperatorPermission.OperateAssets, cancellationToken);
        if (session.Protocol != TeamLabRemoteProtocol.ContainerTerminal || session.Status != TeamLabRemoteSessionStatus.Ready || session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("remote_session_unavailable", "The terminal session is not available.", 409);
        session.ConnectedAt ??= DateTimeOffset.UtcNow;
        session.Status = TeamLabRemoteSessionStatus.Connected;
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            await agents.ProxyRemoteTerminalAsync(session.WorkerNodeId, session.PublicId, session.RuntimeId, session.Generation,
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
            ?? throw new TeamLabApiContractException("remote_session_not_found", "The remote access session was not found.", 404);
        var permission = session.RequestedByUserId == actorId ? TeamLabOperatorPermission.ViewAssets : TeamLabOperatorPermission.OperateAssets;
        await authorization.RequireAsync(session.Runtime.PublicId, actorId, administrator, permission, cancellationToken);
        if (session.Status is TeamLabRemoteSessionStatus.Ended or TeamLabRemoteSessionStatus.Failed) return;
        session.Status = TeamLabRemoteSessionStatus.Ending;
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            await agents.DeleteRemoteRelayAsync(session.WorkerNodeId, session.PublicId, cancellationToken);
            await guacamole.DeleteAsync(session.GuacamoleConnectionId, session.GuacamoleUserId, cancellationToken);
            cache.Remove(ConnectUrlKey(session.PublicId));
        }
        finally
        {
            session.Status = TeamLabRemoteSessionStatus.Ended;
            session.EndedAt = DateTimeOffset.UtcNow;
            session.EndReason = string.IsNullOrWhiteSpace(reason) ? "closed" : reason[..Math.Min(256, reason.Length)];
            events.Record(session.Runtime, "remote-access", TeamLabEventLevel.Info,
                OperationalEventCodes.TeamLab.RemoteSessionEnded, OperationalEventOutcome.Succeeded,
                $"Remote {session.Protocol} session ended for {session.RuntimeAsset.Name}.", workerNodeId: session.WorkerNodeId,
                detail: RemoteDetail(session, session.RuntimeAsset, actorId));
            await context.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task ExpireAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await context.TeamLabRemoteSessions
            .Where(item => item.ExpiresAt <= now &&
                           (item.Status == TeamLabRemoteSessionStatus.Creating ||
                            item.Status == TeamLabRemoteSessionStatus.Ready ||
                            item.Status == TeamLabRemoteSessionStatus.Connected))
            .Take(100)
            .ToArrayAsync(cancellationToken);
        foreach (var session in sessions) session.Status = TeamLabRemoteSessionStatus.Ending;
        if (sessions.Length > 0) await context.SaveChangesAsync(cancellationToken);
        foreach (var session in sessions)
        {
            try { await agents.DeleteRemoteRelayAsync(session.WorkerNodeId, session.PublicId, cancellationToken); }
            catch { }
            try { await guacamole.DeleteAsync(session.GuacamoleConnectionId, session.GuacamoleUserId, cancellationToken); }
            catch { }
            cache.Remove(ConnectUrlKey(session.PublicId));
            session.Status = TeamLabRemoteSessionStatus.Ended;
            session.EndedAt = DateTimeOffset.UtcNow;
            session.EndReason = "expired";
        }
        if (sessions.Length > 0) await context.SaveChangesAsync(cancellationToken);
    }

    public async Task EndRuntimeSessionsAsync(int runtimeId, int generation, string reason, CancellationToken cancellationToken)
    {
        var sessions = await context.TeamLabRemoteSessions
            .Where(item => item.RuntimeId == runtimeId && item.Generation == generation &&
                           (item.Status == TeamLabRemoteSessionStatus.Creating ||
                            item.Status == TeamLabRemoteSessionStatus.Ready ||
                            item.Status == TeamLabRemoteSessionStatus.Connected))
            .ToArrayAsync(cancellationToken);
        foreach (var session in sessions) session.Status = TeamLabRemoteSessionStatus.Ending;
        if (sessions.Length > 0) await context.SaveChangesAsync(cancellationToken);
        foreach (var session in sessions)
        {
            try { await agents.DeleteRemoteRelayAsync(session.WorkerNodeId, session.PublicId, cancellationToken); } catch { }
            try { await guacamole.DeleteAsync(session.GuacamoleConnectionId, session.GuacamoleUserId, cancellationToken); } catch { }
            cache.Remove(ConnectUrlKey(session.PublicId));
            session.Status = TeamLabRemoteSessionStatus.Ended;
            session.EndedAt = DateTimeOffset.UtcNow;
            session.EndReason = reason[..Math.Min(256, reason.Length)];
        }
        if (sessions.Length > 0) await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<TeamLabRuntimeAsset> FindAssetAsync(Guid runtimeId, int assetId, CancellationToken cancellationToken) =>
        await context.TeamLabRuntimeAssets.Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.Id == assetId && item.Runtime.PublicId == runtimeId, cancellationToken)
        ?? throw new TeamLabApiContractException("runtime_asset_not_found", "The runtime asset was not found.", 404);

    private async Task<TeamLabRemoteAccessAvailabilityModel> AvailabilityAsync(TeamLabRuntimeAsset asset, CancellationToken cancellationToken)
    {
        if (asset.Status != TeamLabRuntimeStatus.Running) return new(asset.Id, asset.Name, null, false, "The asset is not running.");
        if (asset.Kind == TeamLabResourceKind.Docker) return new(asset.Id, asset.Name, TeamLabRemoteProtocol.ContainerTerminal, true, null);
        if (asset.Kind != TeamLabResourceKind.Vm || asset.SourceTemplateId is null)
            return new(asset.Id, asset.Name, null, false, "This asset does not support remote operations.");
        var configuration = await context.ImageTemplateRemoteAccesses.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ImageTemplateId == asset.SourceTemplateId.Value, cancellationToken);
        if (configuration is null || !configuration.Enabled)
            return new(asset.Id, asset.Name, null, false, "Remote operations are not configured for this image.");
        if (configuration.CredentialMode == RemoteCredentialMode.PlatformGenerated)
        {
            var credentialReady = await context.TeamLabRuntimeRemoteCredentials.AsNoTracking().AnyAsync(item =>
                item.RuntimeId == asset.RuntimeId && item.Generation == asset.Generation && item.RuntimeAssetId == asset.Id &&
                item.Protocol == configuration.Protocol && item.RevokedAt == null, cancellationToken);
            return credentialReady
                ? new(asset.Id, asset.Name, configuration.Protocol, true, null)
                : new(asset.Id, asset.Name, null, false, "The platform remote account is not ready.");
        }
        if (string.IsNullOrWhiteSpace(configuration.Username) || string.IsNullOrWhiteSpace(configuration.ProtectedSecret))
            return new(asset.Id, asset.Name, null, false, "The image remote account is incomplete.");
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
