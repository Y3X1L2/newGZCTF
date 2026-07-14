using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    TeamLabEventRecorder eventRecorder)
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("GZCTF.TeamLab.WireGuardGrant.v1");
    private readonly PublicUdpGatewayConfig _gateway = gatewayOptions.Value;
    private readonly ContainerProvider _container = containerOptions.Value;

    public async Task<TeamLabAccessGrantModel> CreateAsync(Guid runtimePublicId, CancellationToken cancellationToken)
        => await CreateCoreAsync(runtimePublicId, null, cancellationToken);

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
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        if (runtime.Status != TeamLabRuntimeStatus.Running || runtime.PublicUdpMapping is null)
            throw new TeamLabApiContractException("runtime_not_ready", "The runtime is not ready for access.", 409);
        var entryShard = runtime.Shards.SingleOrDefault(item => item.Id == runtime.EntryShardId && item.Generation == runtime.Generation)
            ?? throw new TeamLabApiContractException("runtime_invalid", "The runtime entry shard is missing.", 500);
        var entryNetwork = runtime.Networks.SingleOrDefault(item => item.ShardId == entryShard.Id && item.Generation == runtime.Generation)
            ?? throw new TeamLabApiContractException("runtime_invalid", "The runtime entry network is missing.", 500);
        var publicEndpoint = !string.IsNullOrWhiteSpace(_gateway.PublicEndpoint)
            ? _gateway.PublicEndpoint.Trim()
            : _container.PublicEntry.Trim();
        if (string.IsNullOrWhiteSpace(publicEndpoint))
            throw new TeamLabApiContractException("capability_unavailable", "The public WireGuard endpoint is not configured.", 409);

        var grant = operationId is { } operation
            ? runtime.AccessGrants.SingleOrDefault(item => item.ApiOperationId == operation)
            : null;
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
                    "access_grant_expired", "The access configuration link is no longer available.", 410);
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
                TeamLabRouteApplicationService.RouterName(runtime.Id, entryShard.Id),
                TeamLabRouteApplicationService.WireGuardName(runtime.Id),
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
                "operation_failed", "The access grant could not be applied to the runtime.", 500);
        var revokedAt = DateTimeOffset.UtcNow;
        foreach (var previous in runtime.AccessGrants.Where(item =>
                     item.Id != grant.Id && item.Generation == runtime.Generation && !item.Revoked))
        {
            previous.Revoked = true;
            previous.RevokedAt = revokedAt;
        }
        grant.AppliedAt = DateTimeOffset.UtcNow;
        runtime.IsOpenToPlayers = true;
        eventRecorder.Record(
            runtime,
            "access",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.AccessOpened,
            OperationalEventOutcome.Succeeded,
            "WireGuard access grant created.",
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
            ?? throw new TeamLabApiContractException("access_grant_not_found", "The access grant was not found.", 404);
        if (grant.ConfigurationConsumedAt is not null || grant.ExpiresAt <= DateTimeOffset.UtcNow ||
            string.IsNullOrWhiteSpace(grant.DownloadTokenHash) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(grant.DownloadTokenHash),
                Encoding.ASCII.GetBytes(HashToken(token))))
            throw new TeamLabApiContractException("access_grant_expired", "The access configuration link is invalid or expired.", 410);
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
            ?? throw new TeamLabApiContractException("access_grant_not_found", "The access grant was not found.", 404);
        if (grant.Revoked)
            return;
        var entryShard = runtime.Shards.Single(item => item.Id == runtime.EntryShardId && item.Generation == runtime.Generation);
        var cleanup = await executor.CleanupShardAsync(entryShard.WorkerNodeId,
            new TeamLabNodeCleanupRequest(runtime.Id, runtime.Generation,
                [TeamLabRouteApplicationService.WireGuardName(runtime.Id)], [], []), cancellationToken);
        if (!cleanup.Success)
            throw new TeamLabApiContractException(
                "operation_failed", "The access grant could not be revoked from the runtime.", 500);
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
            "WireGuard access grant revoked.",
            workerNodeId: entryShard.WorkerNodeId);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<TeamLabRuntime> LoadRuntimeAsync(Guid runtimePublicId, CancellationToken cancellationToken) =>
        await context.TeamLabRuntimes
            .Include(item => item.PublicUdpMapping)
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.AccessGrants)
            .Include(item => item.Events)
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
        ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);

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
