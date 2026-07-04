using System.Security.Cryptography;
using GZCTF.Models.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;
using GZCTF.Models.Internal;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabPeerMaterial(
    TeamLabVpnPeerRuntime Peer,
    string ClientPrivateKey,
    string ServerPrivateKey,
    string ConfigText);

public sealed record TeamLabClientConfigModel(
    int GameId,
    int TeamId,
    string TeamName,
    string Endpoint,
    string ClientAddress,
    string AllowedIPs,
    string Dns,
    int ConfigVersion,
    string ConfigText);

public class TeamLabWireGuardService(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PublicUdpGatewayConfig> gatewayOptions,
    IOptions<ContainerProvider> containerOptions)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("GZCTF.TeamLab.WireGuardPeer.v1");
    private readonly PublicUdpGatewayConfig _gateway = gatewayOptions.Value;
    private readonly ContainerProvider _container = containerOptions.Value;

    public static string ResolvePublicEndpoint(PublicUdpGatewayConfig gateway, ContainerProvider container)
    {
        if (!string.IsNullOrWhiteSpace(gateway.PublicEndpoint))
            return gateway.PublicEndpoint.Trim();

        return container.PublicEntry.Trim();
    }

    public TeamLabPeerMaterial EnsurePeer(TeamLabRuntime runtime, string clientAddress, string allowedIps, string dns)
    {
        if (runtime.PublicUdpMapping is null)
            throw new InvalidOperationException("TeamLab runtime has no public UDP mapping.");

        var publicEndpoint = ResolvePublicEndpoint(_gateway, _container);
        if (string.IsNullOrWhiteSpace(publicEndpoint))
            throw new InvalidOperationException("Public UDP gateway endpoint is not configured.");

        var endpoint = $"{publicEndpoint}:{runtime.PublicUdpMapping.PublicUdpPort}";
        var peer = runtime.VpnPeers.FirstOrDefault(p => !p.Revoked);
        string clientPrivateKey;
        string serverPrivateKey;

        if (peer is null || string.IsNullOrWhiteSpace(peer.ProtectedClientPrivateKey) ||
            string.IsNullOrWhiteSpace(peer.ProtectedServerPrivateKey))
        {
            var client = GenerateKeyPair();
            var server = GenerateKeyPair();
            peer = new TeamLabVpnPeerRuntime
            {
                ClientAddress = clientAddress,
                Endpoint = endpoint,
                AllowedIPs = allowedIps,
                Dns = dns,
                PublicKey = client.PublicKey,
                ProtectedClientPrivateKey = _protector.Protect(client.PrivateKey),
                ServerPublicKey = server.PublicKey,
                ProtectedServerPrivateKey = _protector.Protect(server.PrivateKey),
                ConfigVersion = (runtime.VpnPeers.Max(p => (int?)p.ConfigVersion) ?? 0) + 1
            };
            runtime.VpnPeers.Add(peer);
            clientPrivateKey = client.PrivateKey;
            serverPrivateKey = server.PrivateKey;
        }
        else
        {
            peer.ClientAddress = clientAddress;
            peer.Endpoint = endpoint;
            peer.AllowedIPs = allowedIps;
            peer.Dns = dns;
            clientPrivateKey = _protector.Unprotect(peer.ProtectedClientPrivateKey);
            serverPrivateKey = _protector.Unprotect(peer.ProtectedServerPrivateKey);
        }

        return new TeamLabPeerMaterial(peer, clientPrivateKey, serverPrivateKey,
            BuildClientConfig(clientPrivateKey, peer.ServerPublicKey, clientAddress, endpoint, allowedIps, dns));
    }

    public TeamLabClientConfigModel? BuildClientConfigModel(TeamLabRuntime runtime)
    {
        if (runtime is not { Status: TeamLabRuntimeStatus.Running, IsOpenToPlayers: true })
            return null;

        var peer = runtime.VpnPeers
            .Where(p => !p.Revoked)
            .OrderByDescending(p => p.ConfigVersion)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();

        if (peer is null || string.IsNullOrWhiteSpace(peer.ProtectedClientPrivateKey) ||
            string.IsNullOrWhiteSpace(peer.ServerPublicKey) || string.IsNullOrWhiteSpace(peer.Endpoint) ||
            string.IsNullOrWhiteSpace(peer.ClientAddress) || string.IsNullOrWhiteSpace(peer.AllowedIPs))
            return null;

        try
        {
            var clientPrivateKey = _protector.Unprotect(peer.ProtectedClientPrivateKey);
            var config = BuildClientConfig(clientPrivateKey, peer.ServerPublicKey, peer.ClientAddress, peer.Endpoint,
                peer.AllowedIPs, peer.Dns);

            return new TeamLabClientConfigModel(runtime.GameId, runtime.TeamId,
                runtime.Team?.Name ?? runtime.TeamId.ToString(), peer.Endpoint, peer.ClientAddress, peer.AllowedIPs,
                peer.Dns, peer.ConfigVersion, config);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static (string PrivateKey, string PublicKey) GenerateKeyPair()
    {
        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        var privateKey = (X25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (X25519PublicKeyParameters)keyPair.Public;
        return (Base64.ToBase64String(privateKey.GetEncoded()), Base64.ToBase64String(publicKey.GetEncoded()));
    }

    public static string BuildClientConfig(string privateKey, string serverPublicKey, string address,
        string endpoint, string allowedIps, string dns)
    {
        var dnsLine = string.IsNullOrWhiteSpace(dns)
            ? string.Empty
            : $"{Environment.NewLine}DNS = {dns.Trim()}";

        return $"""
        [Interface]
        PrivateKey = {privateKey}
        Address = {address}{dnsLine}

        [Peer]
        PublicKey = {serverPublicKey}
        AllowedIPs = {allowedIps}
        Endpoint = {endpoint}
        PersistentKeepalive = 25
        """.Trim();
    }
}
