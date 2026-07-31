using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Vm;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.RemoteAccess;

public sealed class RemoteAccessRelayService(
    KvmService kvm,
    IOptions<AgentConfig> agentConfig,
    IOptions<KvmConfig> kvmConfig,
    ILogger<RemoteAccessRelayService> logger)
{
    private const int FirstPort = 47000;
    private const int PortCount = 1000;
    private readonly ConcurrentDictionary<Guid, Relay> _relays = new();
    // Reuse the operator-configured console source policy. ServerUrl is only an additional
    // literal-address convenience and must not become the sole path to authorize Guacamole.
    private readonly RdpProxyAccessPolicy _sourcePolicy = RdpProxyAccessPolicy.Create(
        kvmConfig.Value.RdpProxyAllowedSources, agentConfig.Value.ServerUrl);

    public async Task<RemoteRelayResponse> CreateAsync(CreateRemoteRelayRequest request, CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty || request.RuntimeId <= 0 || request.Generation <= 0 ||
            request.TargetPort is < 1 or > 65535 || request.ExpiresAt <= DateTimeOffset.UtcNow ||
            request.ExpiresAt > DateTimeOffset.UtcNow.AddHours(2) ||
            !IPAddress.TryParse(request.TargetAddress, out var targetAddress))
            throw new AgentOperationException("RemoteAccess", "remote_access.invalid_request", "The remote access relay request is invalid.", false);

        var verified = await kvm.ExecuteWithIdentityAsync(
            request.VmName, request.Generation, request.NativeId,
            token => kvm.GetIpAddressWithDiagnosticAsync(request.VmName, token), cancellationToken);
        if (!IPAddress.TryParse(verified.IpAddress, out var guestAddress) || !guestAddress.Equals(targetAddress))
            throw new AgentOperationException("RemoteAccess", "remote_access.asset_identity_mismatch", "The requested target does not match the active VM identity.", false);

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
                var relay = new Relay(request.SessionId, port, targetAddress, request.TargetPort, request.ExpiresAt,
                    listener, _sourcePolicy, _relays, logger);
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
        if (_relays.TryRemove(sessionId, out var relay)) relay.Dispose();
        return Task.CompletedTask;
    }

    private sealed class Relay(
        Guid sessionId, int port, IPAddress targetAddress, int targetPort, DateTimeOffset expiresAt,
        TcpListener listener, RdpProxyAccessPolicy sourcePolicy, ConcurrentDictionary<Guid, Relay> owner,
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
                    try { client = await listener.AcceptTcpClientAsync(linked.Token); }
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
                using var target = new TcpClient();
                try
                {
                    await target.ConnectAsync(targetAddress, targetPort, token);
                    await using var source = client.GetStream();
                    await using var destination = target.GetStream();
                    await Task.WhenAny(source.CopyToAsync(destination, token), destination.CopyToAsync(source, token));
                }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
            }
        }
    }
}
