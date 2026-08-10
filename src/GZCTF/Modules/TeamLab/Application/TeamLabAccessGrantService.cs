using System.Net;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabAccessConfigurationResult(string FileName, string Configuration);

public sealed class TeamLabAccessGrantService(
    AppDbContext context,
    ITeamLabNodeExecutor executor,
    IDataProtectionProvider protectionProvider,
    IOptions<PublicUdpGatewayConfig> gatewayOptions,
    IOptions<ContainerProvider> containerOptions,
    IDistributedLeaseProvider locks,
    TeamLabEventRecorder eventRecorder)
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("GZCTF.TeamLab.WireGuardGrant.v1");
    private readonly PublicUdpGatewayConfig _gateway = gatewayOptions.Value;
    private readonly ContainerProvider _container = containerOptions.Value;

    public async Task<TeamLabAccessGrantModel> CreateAsync(Guid runtimePublicId, CancellationToken cancellationToken)
        => await CreateCoreAsync(runtimePublicId, null, cancellationToken);

    public async Task<IReadOnlyList<TeamLabAccessGrantModel>> ListAsync(
        Guid runtimePublicId,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return runtime.AccessGrants
            .Where(item => item.Generation == runtime.Generation && !item.Revoked && item.ExpiresAt > now)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item =>
            {
                var token = item.ConfigurationConsumedAt is null && !string.IsNullOrWhiteSpace(item.ProtectedDownloadToken)
                    ? _protector.Unprotect(item.ProtectedDownloadToken)
                    : null;
                return ToModel(runtime, item, token is null ? null : DownloadUrl(runtime, item, token));
            })
            .ToArray();
    }

    public async Task<TeamLabAccessGrantModel> CreateForOperationAsync(
        Guid runtimePublicId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        await CreateCoreAsync(runtimePublicId, operationId, cancellationToken);

    private async Task<TeamLabAccessGrantModel> CreateCoreAsync(
        Guid runtimePublicId,
        Guid? operationId,
        CancellationToken cancellationToken)
    {
        await using var accessLease = await locks.AcquireAsync(
            $"teamlab:access-grant:{runtimePublicId:D}",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(2),
            cancellationToken);
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        if (runtime.Status != TeamLabRuntimeStatus.Running || runtime.PublicUdpMapping is null)
            throw new TeamLabApiContractException("runtime_not_ready", "运行时尚未就绪，无法访问", 409);
        var entryShard = runtime.Shards.SingleOrDefault(item => item.Id == runtime.EntryShardId && item.Generation == runtime.Generation)
            ?? throw new TeamLabApiContractException("runtime_invalid", "运行时入口分片缺失", 500);
        var entryNetwork = ResolveEntryNetwork(runtime, entryShard);
        var publicEndpoint = !string.IsNullOrWhiteSpace(_gateway.PublicEndpoint)
            ? _gateway.PublicEndpoint.Trim()
            : _container.PublicEntry.Trim();
        if (string.IsNullOrWhiteSpace(publicEndpoint))
            throw new TeamLabApiContractException("capability_unavailable", "未配置公共 WireGuard 端点", 409);

        var grant = operationId is { } operation
            ? runtime.AccessGrants.SingleOrDefault(item => item.ApiOperationId == operation)
            : null;
        var activeGrant = runtime.AccessGrants.SingleOrDefault(item =>
            item.Generation == runtime.Generation && !item.Revoked && item.ExpiresAt > DateTimeOffset.UtcNow);
        if (grant is null && activeGrant is not null)
        {
            if (activeGrant.ConfigurationConsumedAt is not null ||
                string.IsNullOrWhiteSpace(activeGrant.ProtectedDownloadToken))
                throw new TeamLabApiContractException(
                    "access_grant_already_active",
                    "已存在活跃的访问授权，轮换团队 VPN 密钥前请先显式撤销",
                    409);
            var existingToken = _protector.Unprotect(activeGrant.ProtectedDownloadToken);
            return ToModel(runtime, activeGrant, DownloadUrl(runtime, activeGrant, existingToken));
        }
        string token;
        if (grant is null)
        {
            var client = GenerateKeyPair();
            var server = GenerateKeyPair();
            token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var network = IPNetwork.Parse(entryNetwork.Cidr);
            grant = new TeamLabAccessGrant
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                ApiOperationId = operationId,
                Type = TeamLabAccessGrantType.WireGuard,
                ClientAddress = $"{HostAt(network, 2)}/32",
                Endpoint = $"{publicEndpoint}:{runtime.PublicUdpMapping.PublicUdpPort}",
                AllowedIps = entryNetwork.Cidr,
                Dns = entryNetwork.GatewayIp,
                PublicKey = client.PublicKey,
                ProtectedPrivateKey = _protector.Protect(client.PrivateKey),
                ServerPublicKey = server.PublicKey,
                ProtectedServerPrivateKey = _protector.Protect(server.PrivateKey),
                DownloadTokenHash = HashToken(token),
                ProtectedDownloadToken = _protector.Protect(token),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(12)
            };
            runtime.AccessGrants.Add(grant);
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(grant.ProtectedDownloadToken))
                throw new TeamLabApiContractException(
                    "access_grant_expired", "访问配置链接已失效", 410);
            token = _protector.Unprotect(grant.ProtectedDownloadToken);
            if (grant.AppliedAt is not null)
                return ToModel(runtime, grant, DownloadUrl(runtime, grant, token));
        }

        var serverAddress = $"{LastHost(IPNetwork.Parse(entryNetwork.Cidr))}/32";
        var blocked = runtime.Networks.Where(item => item.Generation == runtime.Generation && item.Id != entryNetwork.Id)
            .Select(item => item.Cidr).ToArray();
        var applied = await executor.ConfigureAccessAsync(entryShard.WorkerNodeId,
            new TeamLabNodeAccessApplyRequest(
                runtime.Id,
                runtime.Generation,
                TeamLabResourceNameFactory.RouterNamespace(runtime.Id, entryShard.Id),
                TeamLabResourceNameFactory.WireGuardInterface(runtime.Id),
                runtime.PublicUdpMapping.WorkerWireGuardPort,
                serverAddress,
                _protector.Unprotect(grant.ProtectedServerPrivateKey),
                grant.PublicKey,
                grant.ClientAddress,
                entryNetwork.Cidr,
                [entryNetwork.Cidr],
                blocked), cancellationToken);
        if (!applied.Success)
            throw new TeamLabApiContractException(
                "operation_failed", "无法将访问授权应用到运行时", 500);
        grant.AppliedAt = DateTimeOffset.UtcNow;
        runtime.IsOpenToPlayers = true;
        eventRecorder.Record(
            runtime,
            "access",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.AccessOpened,
            OperationalEventOutcome.Succeeded,
            "已创建 WireGuard 访问授权",
            workerNodeId: entryShard.WorkerNodeId);
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(runtime, grant, DownloadUrl(runtime, grant, token));
    }

    public async Task<TeamLabAccessGrantModel?> GetOperationResultAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var grant = await context.TeamLabAccessGrants.AsNoTracking()
            .Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.ApiOperationId == operationId, cancellationToken);
        if (grant is null)
            return null;
        var token = grant.ConfigurationConsumedAt is null && grant.ExpiresAt > DateTimeOffset.UtcNow &&
                    !string.IsNullOrWhiteSpace(grant.ProtectedDownloadToken)
            ? _protector.Unprotect(grant.ProtectedDownloadToken)
            : null;
        return ToModel(grant.Runtime, grant,
            token is null ? null : DownloadUrl(grant.Runtime, grant, token));
    }

    public async Task<TeamLabAccessConfigurationResult> ConsumeConfigurationAsync(
        Guid runtimePublicId,
        Guid grantPublicId,
        string token,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        var grant = runtime.AccessGrants.SingleOrDefault(item => item.PublicId == grantPublicId &&
                                                                 item.Generation == runtime.Generation && !item.Revoked)
            ?? throw new TeamLabApiContractException("access_grant_not_found", "未找到访问授权", 404);
        if (grant.ConfigurationConsumedAt is not null || grant.ExpiresAt <= DateTimeOffset.UtcNow ||
            string.IsNullOrWhiteSpace(grant.DownloadTokenHash) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(grant.DownloadTokenHash),
                Encoding.ASCII.GetBytes(HashToken(token))))
            throw new TeamLabApiContractException("access_grant_expired", "访问配置链接无效或已过期", 410);
        var clientPrivate = _protector.Unprotect(grant.ProtectedPrivateKey);
        var config = BuildClientConfig(clientPrivate, grant.ServerPublicKey, grant.ClientAddress, grant.Endpoint,
            grant.AllowedIps, grant.Dns);
        grant.ConfigurationConsumedAt = DateTimeOffset.UtcNow;
        grant.ProtectedDownloadToken = null;
        await context.SaveChangesAsync(cancellationToken);
        return new TeamLabAccessConfigurationResult($"tl-{runtime.PublicId:N}"[..11] + ".conf", config);
    }

    public async Task RevokeAsync(Guid runtimePublicId, Guid grantPublicId, CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        var grant = runtime.AccessGrants.SingleOrDefault(item => item.PublicId == grantPublicId)
            ?? throw new TeamLabApiContractException("access_grant_not_found", "未找到访问授权", 404);
        if (grant.Revoked)
            return;
        var entryShard = runtime.Shards.Single(item => item.Id == runtime.EntryShardId && item.Generation == runtime.Generation);
        var cleanup = await executor.RemoveAccessAsync(entryShard.WorkerNodeId,
            new TeamLabNodeAccessRemoveRequest(
                runtime.Id,
                runtime.Generation,
                TeamLabResourceNameFactory.RouterNamespace(runtime.Id, entryShard.Id),
                TeamLabResourceNameFactory.WireGuardInterface(runtime.Id)),
            cancellationToken);
        if (!cleanup.Success)
            throw new TeamLabApiContractException(
                "operation_failed", "无法从运行时撤销访问授权", 500);
        grant.Revoked = true;
        grant.RevokedAt = DateTimeOffset.UtcNow;
        runtime.IsOpenToPlayers = runtime.AccessGrants.Any(item => item.Id != grant.Id &&
                                                                   item.Generation == runtime.Generation && !item.Revoked);
        eventRecorder.Record(
            runtime,
            "access",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.AccessRevoked,
            OperationalEventOutcome.Succeeded,
            "已撤销 WireGuard 访问授权",
            workerNodeId: entryShard.WorkerNodeId);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(Guid runtimePublicId, CancellationToken cancellationToken)
    {
        var grantIds = await context.TeamLabAccessGrants.AsNoTracking()
            .Where(item => item.Runtime.PublicId == runtimePublicId &&
                           item.Generation == item.Runtime.Generation && !item.Revoked)
            .Select(item => item.PublicId)
            .ToArrayAsync(cancellationToken);
        foreach (var grantId in grantIds)
            await RevokeAsync(runtimePublicId, grantId, cancellationToken);
    }

    private async Task<TeamLabRuntime> LoadRuntimeAsync(Guid runtimePublicId, CancellationToken cancellationToken) =>
        await context.TeamLabRuntimes
            .Include(item => item.PublicUdpMapping)
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.AccessGrants)
            .Include(item => item.Events)
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
        ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);

    internal static TeamLabRuntimeNetwork ResolveEntryNetwork(
        TeamLabRuntime runtime,
        TeamLabRuntimeShard entryShard)
    {
        var entryNetworks = runtime.Networks
            .Where(item => item.Generation == runtime.Generation && item.IsEntry)
            .ToArray();
        if (entryNetworks.Length != 1 || entryNetworks[0].ShardId != entryShard.Id)
            throw new TeamLabApiContractException(
                "runtime_invalid", "运行时入口网络缺失或分配到了错误的分片", 500);
        return entryNetworks[0];
    }

    private static TeamLabAccessGrantModel ToModel(TeamLabRuntime runtime, TeamLabAccessGrant grant, string? downloadUrl) =>
        new(grant.PublicId, "WireGuard", grant.ClientAddress, grant.Endpoint, grant.AllowedIps, grant.Dns,
            grant.CreatedAt, grant.ExpiresAt, downloadUrl);

    private static string DownloadUrl(TeamLabRuntime runtime, TeamLabAccessGrant grant, string token) =>
        $"/api/open/v1/teamlab/runtimes/{runtime.PublicId:D}/access-grants/{grant.PublicId:D}/download?token={token}";

    private static (string PrivateKey, string PublicKey) GenerateKeyPair()
    {
        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        return (
            Base64.ToBase64String(((X25519PrivateKeyParameters)pair.Private).GetEncoded()),
            Base64.ToBase64String(((X25519PublicKeyParameters)pair.Public).GetEncoded()));
    }

    private static string BuildClientConfig(string privateKey, string serverPublicKey, string address,
        string endpoint, string allowedIps, string dns) =>
        $"""
        [Interface]
        PrivateKey = {privateKey}
        Address = {address}
        DNS = {dns}

        [Peer]
        PublicKey = {serverPublicKey}
        AllowedIPs = {allowedIps}
        Endpoint = {endpoint}
        PersistentKeepalive = 25
        """.Trim();

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    private static IPAddress HostAt(IPNetwork network, uint offset)
    {
        var bytes = network.BaseAddress.GetAddressBytes();
        var raw = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var value = raw + offset;
        return new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
    }

    private static IPAddress LastHost(IPNetwork network)
    {
        var size = 1u << (32 - network.PrefixLength);
        return HostAt(network, size - 2);
    }
}
