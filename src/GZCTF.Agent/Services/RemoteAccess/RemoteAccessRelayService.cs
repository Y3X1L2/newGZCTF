using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Vm;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.RemoteAccess;

public sealed class RemoteAccessRelayService(
    KvmService kvm,
    IOptions<AgentConfig> agentConfig,
    IOptions<KvmConfig> kvmConfig,
    TeamLabTerminalSessionRegistry terminals,
    ILogger<RemoteAccessRelayService> logger)
{
    private const int FirstPort = 47000;
    private const int PortCount = 1000;
    private const int SocketLevel = 1; // Linux SOL_SOCKET.
    private const int BindToDevice = 25; // Linux SO_BINDTODEVICE.
    private readonly ConcurrentDictionary<Guid, Relay> _relays = new();

    public Guid[] ActiveSessionIds() => _relays.Where(item => item.Value.ExpiresAt > DateTimeOffset.UtcNow)
        .Select(item => item.Key).Concat(terminals.ActiveSessionIds()).Distinct().ToArray();
    // Reuse the operator-configured console source policy. ServerUrl is only an additional
    // literal-address convenience and must not become the sole path to authorize Guacamole.
    private readonly RdpProxyAccessPolicy _sourcePolicy = RdpProxyAccessPolicy.Create(
        kvmConfig.Value.RdpProxyAllowedSources, agentConfig.Value.ServerUrl);

    public async Task<RemoteRelayResponse> CreateAsync(CreateRemoteRelayRequest request, CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty || request.RuntimeId <= 0 || request.Generation <= 0 ||
            request.ExpiresAt <= DateTimeOffset.UtcNow || request.ExpiresAt > DateTimeOffset.UtcNow.AddHours(2))
            throw new AgentOperationException("RemoteAccess", "remote_access.invalid_request", "The remote access relay request is invalid.", false);
        IPAddress managementAddress;
        string? sourceInterface = null;
        var targetPort = request.TargetPort;
        if (request.VncConsole)
        {
            managementAddress = IPAddress.Loopback;
            targetPort = await kvm.GetVncConsolePortAsync(request.VmName, request.Generation, request.NativeId, cancellationToken);
            await EnsureVncConsoleAsync(targetPort, cancellationToken);
        }
        else
        {
            if (request.TargetPort is < 1 or > 65535 || !IPAddress.TryParse(request.TargetAddress, out var targetAddress))
                throw new AgentOperationException("RemoteAccess", "remote_access.invalid_request", "The remote access relay target is invalid.", false);
            if (!Guid.TryParse(request.NativeId, out var nativeId))
                throw new AgentOperationException("RemoteAccess", "remote_access.invalid_request", "The VM identity is invalid.", false);
            var identity = new GZCTF.TeamLab.Contracts.TeamLabVmDiagnosticsRequest(
                request.VmName, request.Generation, nativeId);
            var management = await kvm.ExecuteWithTeamLabIdentityAsync(identity,
                token => kvm.GetManagementIpAddressWithDiagnosticAsync(request.VmName, token), cancellationToken);
            if (!IPAddress.TryParse(management.IpAddress, out managementAddress!))
            {
                managementAddress = targetAddress;
                sourceInterface = FindSourceInterface(targetAddress);
            }
            await EnsureTargetReachableAsync(managementAddress, request.TargetPort, sourceInterface, cancellationToken);
        }

        if (_relays.TryGetValue(request.SessionId, out var existing))
            return new RemoteRelayResponse(request.SessionId, existing.Port, existing.ExpiresAt);

        var start = (request.SessionId.GetHashCode() & int.MaxValue) % PortCount;
        for (var offset = 0; offset < PortCount; offset++)
        {
            var port = FirstPort + ((start + offset) % PortCount);
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start(16);
                var relay = new Relay(request.SessionId, port, managementAddress, targetPort, request.ExpiresAt,
                    listener, sourceInterface, _sourcePolicy, _relays, logger);
                if (_relays.TryAdd(request.SessionId, relay))
                {
                    relay.Start();
                    return new RemoteRelayResponse(request.SessionId, port, request.ExpiresAt);
                }
                listener.Stop();
                return new RemoteRelayResponse(request.SessionId, existing?.Port ?? port, request.ExpiresAt);
            }
            catch (SocketException) { }
        }
        throw new AgentOperationException("RemoteAccess", "remote_access.no_port", "No temporary relay port is available.", true);
    }

    public Task DeleteAsync(Guid sessionId)
    {
        if (_relays.TryRemove(sessionId, out var relay))
            relay.Dispose();
        return Task.CompletedTask;
    }

    public Task CancelTerminalAsync(Guid sessionId) => terminals.CancelAndWaitAsync(sessionId);

    internal static async Task EnsureVncConsoleAsync(int port, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            var banner = new byte[12];
            await client.GetStream().ReadExactlyAsync(banner, deadline.Token);
            if (!IsVncBanner(banner))
                throw new IOException("Invalid VNC protocol banner.");
        }
        catch (Exception exception) when (!token.IsCancellationRequested && exception is SocketException or IOException or OperationCanceledException)
        {
            throw new AgentOperationException("RemoteAccess", "remote_access.console_unavailable", "The VNC console did not complete its protocol probe.", true);
        }
    }

    internal static bool IsVncBanner(byte[] bytes) => bytes.Length == 12 &&
        System.Text.Encoding.ASCII.GetString(bytes) is "RFB 003.003\n" or "RFB 003.007\n" or "RFB 003.008\n";

    private static async Task EnsureTargetReachableAsync(
        IPAddress address, int port, string? sourceInterface, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient(address.AddressFamily);
        BindToInterface(client.Client, sourceInterface);
        try
        {
            await client.ConnectAsync(address, port, deadline.Token);
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new AgentOperationException("RemoteAccess", "remote_access.target_unreachable",
                $"The VM management service at {address}:{port} is not reachable.", false);
        }
    }

    private static string? FindSourceInterface(IPAddress target) =>
        NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(network => network.GetIPProperties().UnicastAddresses
                .Select(address => (network.Name, Address: address)))
            .Where(item => item.Address.Address.AddressFamily == target.AddressFamily &&
                           SharesPrefix(item.Address.Address, target, item.Address.PrefixLength))
            .OrderByDescending(item => item.Address.PrefixLength)
            .Select(item => item.Name)
            .FirstOrDefault();

    private static void BindToInterface(Socket socket, string? interfaceName)
    {
        if (interfaceName is not null)
            socket.SetRawSocketOption(SocketLevel, BindToDevice,
                Encoding.UTF8.GetBytes($"{interfaceName}\0"));
    }

    private static bool SharesPrefix(IPAddress left, IPAddress right, int prefixLength)
    {
        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        var wholeBytes = prefixLength / 8;
        if (!leftBytes.AsSpan(0, wholeBytes).SequenceEqual(rightBytes.AsSpan(0, wholeBytes)))
            return false;
        var remainingBits = prefixLength % 8;
        return remainingBits == 0 ||
               (leftBytes[wholeBytes] >> (8 - remainingBits)) == (rightBytes[wholeBytes] >> (8 - remainingBits));
    }

    private sealed class Relay(
        Guid sessionId, int port, IPAddress targetAddress, int targetPort, DateTimeOffset expiresAt,
        TcpListener listener, string? sourceInterface, RdpProxyAccessPolicy sourcePolicy,
        ConcurrentDictionary<Guid, Relay> owner,
        ILogger logger) : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        public int Port => port;
        public DateTimeOffset ExpiresAt => expiresAt;
        public void Start() => _ = RunAsync();
        public void Dispose() { _stop.Cancel(); listener.Stop(); }

        private async Task RunAsync()
        {
            try
            {
                using var expiry = new CancellationTokenSource(expiresAt - DateTimeOffset.UtcNow);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, expiry.Token);
                while (!linked.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    { client = await listener.AcceptTcpClientAsync(linked.Token); }
                    catch (OperationCanceledException) { break; }
                    if (!sourcePolicy.IsAllowed(((IPEndPoint?)client.Client.RemoteEndPoint)?.Address))
                    {
                        client.Dispose();
                        continue;
                    }
                    _ = ForwardAsync(client, linked.Token);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "TeamLab remote relay {SessionId} failed", sessionId);
            }
            finally
            {
                owner.TryRemove(sessionId, out _);
                listener.Stop();
            }
        }

        private async Task ForwardAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                using var target = new TcpClient(targetAddress.AddressFamily);
                BindToInterface(target.Client, sourceInterface);
                try
                {
                    await target.ConnectAsync(targetAddress, targetPort, token);
                    await using var source = client.GetStream();
                    await using var destination = target.GetStream();
                    await Task.WhenAny(source.CopyToAsync(destination, token), destination.CopyToAsync(source, token));
                }
                catch (OperationCanceledException) { }
                catch (SocketException exception)
                {
                    logger.LogWarning(exception, "TeamLab remote relay {SessionId} lost its guest connection", sessionId);
                }
            }
        }
    }
}
