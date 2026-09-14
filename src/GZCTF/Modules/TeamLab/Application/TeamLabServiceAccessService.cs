using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabServiceAccessGateway
{
    Task ApplyAsync(Guid nodeId, GZCTF.TeamLab.Contracts.TeamLabServiceForwardRequest request, CancellationToken token);
    Task RemoveAsync(Guid nodeId, GZCTF.TeamLab.Contracts.TeamLabServiceForwardRequest request, CancellationToken token);
}

public interface ITeamLabServiceAccessCleanup
{
    Task<string[]> CleanupRuntimeAsync(Guid runtimeId, CancellationToken token);
}

public sealed class TeamLabServiceAccessService(
    AppDbContext context,
    IPortAllocationService ports,
    IPublicUdpGatewayProvider publicGateway,
    ITeamLabServiceAccessGateway nodeGateway,
    IOptions<PublicUdpGatewayConfig> options) : ITeamLabServiceAccessCleanup
{
    private readonly PublicUdpGatewayConfig gateway = options.Value;

    public async Task<TeamLabServiceAccessModel> CreateAsync(
        Guid runtimeId, int assetId, CreateTeamLabServiceAccessModel command, CancellationToken token)
    {
        var protocol = command.Protocol.Trim().ToLowerInvariant();
        if (protocol is not ("tcp" or "udp") || command.InternalPort is < 1 or > 65535)
            throw new TeamLabApiContractException("service_access_invalid", "协议或内部端口无效", 422);
        if (string.IsNullOrWhiteSpace(gateway.PublicEndpoint))
            throw new TeamLabApiContractException("service_access_unavailable", "服务器尚未配置公网入口地址", 409);
        var runtime = await context.TeamLabRuntimes.Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        var asset = runtime.Assets.SingleOrDefault(item => item.Id == assetId && item.Generation == runtime.Generation)
            ?? throw new TeamLabApiContractException("runtime_asset_not_found", "未找到运行资产", 404);
        if (runtime.Status != TeamLabRuntimeStatus.Running || asset.WorkerNodeId is null || string.IsNullOrWhiteSpace(asset.IpAddress))
            throw new TeamLabApiContractException("service_access_unavailable", "资产尚未运行或没有可访问地址", 409);
        var networkKeys = TeamLabLinkPolicyService.AssetNetworkKeys(asset);
        var networkKey = string.IsNullOrWhiteSpace(command.NetworkKey) ? networkKeys.FirstOrDefault() : command.NetworkKey.Trim();
        if (networkKey is null || !networkKeys.Contains(networkKey))
            throw new TeamLabApiContractException("service_access_network_invalid", "资产未连接所选网段", 422);
        var worker = await context.WorkerNodes.AsNoTracking().SingleAsync(item => item.Id == asset.WorkerNodeId, token);
        if (string.IsNullOrWhiteSpace(worker.TeamLabTunnelIp))
            throw new TeamLabApiContractException("service_access_unavailable", "节点尚未配置 TeamLab 隧道地址", 409);

        var access = new TeamLabServiceAccess
        {
            RuntimeId = runtime.Id, Generation = runtime.Generation, RuntimeAssetId = asset.Id,
            WorkerNodeId = worker.Id, NetworkKey = networkKey, Protocol = protocol,
            InternalPort = command.InternalPort
        };
        PortLease? lease = null;
        if (command.PublicPort is { } requested)
        {
            if (!await ports.ReserveExistingPortAsync(requested, access.PublicId, token))
                throw new TeamLabApiContractException("service_access_port_unavailable", "指定公网端口不可用", 409);
            access.PublicPort = requested;
            access.PortLeaseId = access.PublicId;
        }
        else
        {
            lease = await ports.AllocatePortAsync(access.PublicId, token)
                ?? throw new TeamLabApiContractException("service_access_port_unavailable", "没有可用的公网端口", 409);
            access.PublicPort = lease.Port;
            access.PortLeaseId = lease.LeaseId;
        }
        context.TeamLabServiceAccesses.Add(access);
        await context.SaveChangesAsync(token);
        var forward = Forward(access, asset.IpAddress);
        var workerApplied = false;
        try
        {
            await nodeGateway.ApplyAsync(worker.Id, forward, token);
            workerApplied = true;
            var synced = await publicGateway.SyncServiceAsync(
                new(access.PublicId, protocol, access.PublicPort, worker.TeamLabTunnelIp, access.PublicPort), token);
            if (!synced.Success) throw new InvalidOperationException(synced.Message);
            access.Status = "active";
        }
        catch (Exception exception)
        {
            if (workerApplied)
                await nodeGateway.RemoveAsync(worker.Id, forward, token);
            access.Status = "failed";
            access.LastError = exception.Message.Length <= 1024 ? exception.Message : exception.Message[..1024];
            access.RevokedAt = DateTimeOffset.UtcNow;
            await ports.ReleasePortAsync(access.PublicPort, access.PortLeaseId, token);
        }
        await context.SaveChangesAsync(token);
        return Model(access, runtime.PublicId, asset.Name);
    }

    public async Task<IReadOnlyList<TeamLabServiceAccessModel>> ListAsync(Guid runtimeId, CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking().SingleOrDefaultAsync(item => item.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        var accesses = await context.TeamLabServiceAccesses.AsNoTracking()
            .Include(item => item.RuntimeAsset)
            .Where(item => item.RuntimeId == runtime.Id)
            .OrderByDescending(item => item.Id)
            .ToArrayAsync(token);
        return accesses.Select(item => Model(item, runtimeId, item.RuntimeAsset.Name)).ToArray();
    }

    public async Task<TeamLabServiceAccessModel> RemoveAsync(Guid runtimeId, Guid accessId, CancellationToken token)
    {
        var access = await context.TeamLabServiceAccesses.Include(item => item.Runtime).Include(item => item.RuntimeAsset)
            .SingleOrDefaultAsync(item => item.PublicId == accessId && item.Runtime.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("service_access_not_found", "未找到服务开放记录", 404);
        if (access.RevokedAt is null)
        {
            var worker = await context.WorkerNodes.AsNoTracking().SingleAsync(item => item.Id == access.WorkerNodeId, token);
            var mapping = new PublicServiceGatewayMapping(access.PublicId, access.Protocol, access.PublicPort, worker.TeamLabTunnelIp!, access.PublicPort);
            var gatewayResult = await publicGateway.RemoveServiceAsync(mapping, token);
            if (!gatewayResult.Success)
                throw new InvalidOperationException(gatewayResult.Message);
            await nodeGateway.RemoveAsync(access.WorkerNodeId, Forward(access, access.RuntimeAsset.IpAddress!), token);
            await ports.ReleasePortAsync(access.PublicPort, access.PortLeaseId, token);
            access.Status = "revoked";
            access.RevokedAt = DateTimeOffset.UtcNow;
            access.LastError = null;
            await context.SaveChangesAsync(token);
        }
        return Model(access, runtimeId, access.RuntimeAsset.Name);
    }

    public async Task<string[]> CleanupRuntimeAsync(Guid runtimeId, CancellationToken token)
    {
        var ids = await context.TeamLabServiceAccesses.AsNoTracking()
            .Where(item => item.Runtime.PublicId == runtimeId && item.RevokedAt == null)
            .Select(item => item.PublicId).ToArrayAsync(token);
        var errors = new List<string>();
        foreach (var id in ids)
            try { await RemoveAsync(runtimeId, id, token); }
            catch (Exception exception) { errors.Add(exception.Message); }
        return errors.ToArray();
    }

    private static GZCTF.TeamLab.Contracts.TeamLabServiceForwardRequest Forward(TeamLabServiceAccess access, string address) =>
        new(access.PublicId, access.RuntimeId, access.Generation, access.Protocol, access.PublicPort, address, access.InternalPort);

    private TeamLabServiceAccessModel Model(TeamLabServiceAccess access, Guid runtimeId, string assetName) => new(
        access.PublicId, runtimeId, access.Generation, access.RuntimeAssetId, assetName, access.NetworkKey,
        access.Protocol, access.InternalPort, access.PublicPort, $"{gateway.PublicEndpoint}:{access.PublicPort}",
        access.Status, access.LastError, access.CreatedAt, access.RevokedAt);
}
