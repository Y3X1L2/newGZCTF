using System.Collections.Concurrent;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;
using SharpPcap;

namespace GZCTF.Agent.Services.Observation;

public sealed class TeamLabPacketObserver(
    ObservationPointRegistry registry,
    ObservationBatchSpool spool,
    EndpointSensorChannelService endpointSensors,
    IOptions<AgentTeamLabConfig> options,
    ILogger<TeamLabPacketObserver> logger) : BackgroundService
{
    private readonly AgentTeamLabConfig _config = options.Value;
    private readonly ConcurrentDictionary<string, CaptureSession> _sessions = new(StringComparer.Ordinal);
    private readonly FlowAccumulator _flows = new(options.Value.ObservationMaxActiveFlows);
    private long _parserFailures;
    private long _captureFailures;
    private string? _lastError;

    public TeamLabObservationBatchResponse Read(TeamLabObservationBatchRequest request) =>
        spool.Read(request, Health());

    public Task AcknowledgeAsync(int runtimeId, int generation, long sequence, CancellationToken token) =>
        spool.AcknowledgeAsync(runtimeId, generation, sequence, token);

    public TeamLabObservationHealth Health()
    {
        var flow = _flows.Snapshot();
        var registrations = registry.Snapshot();
        var sensorRejections = endpointSensors.RejectionSnapshot();
        return new TeamLabObservationHealth(
            !_config.DryRun && _config.Enable,
            registrations.Count,
            _sessions.Count,
            flow.ActiveCount,
            Interlocked.Read(ref _captureFailures) + flow.EvictedCount,
            Interlocked.Read(ref _parserFailures),
            sensorRejections.Count,
            0,
            sensorRejections.LastCode,
            _lastError);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await registry.LoadAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Reconcile();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or PcapException or UnauthorizedAccessException)
            {
                _lastError = Trim(exception.Message);
                logger.LogWarning(exception, "TeamLab packet observer reconciliation failed.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();
        await base.StopAsync(cancellationToken);
    }

    private void Reconcile()
    {
        var desired = registry.Snapshot()
            .GroupBy(item => item.InterfaceName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ObservationPointRegistration>)group.ToArray(),
                StringComparer.Ordinal);
        foreach (var stale in _sessions.Keys.Where(key => !desired.ContainsKey(key)).ToArray())
            if (_sessions.TryRemove(stale, out var session)) session.Dispose();
        foreach (var (interfaceName, registrations) in desired)
        {
            if (_sessions.TryGetValue(interfaceName, out var current))
            {
                current.Update(registrations);
                continue;
            }
            if (_config.DryRun || !_config.Enable) continue;
            try
            {
                var session = Start(interfaceName, registrations);
                if (!_sessions.TryAdd(interfaceName, session)) session.Dispose();
                _lastError = null;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or PcapException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref _captureFailures);
                _lastError = Trim(exception.Message);
                logger.LogDebug(exception,
                    "TeamLab observation interface {InterfaceName} is not ready yet.", interfaceName);
            }
        }
    }

    private CaptureSession Start(
        string interfaceName,
        IReadOnlyList<ObservationPointRegistration> registrations)
    {
        var devices = CaptureDeviceList.Instance;
        devices.Refresh();
        var device = devices
            .FirstOrDefault(item => string.Equals(item.Name, interfaceName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Managed observation interface '{interfaceName}' is unavailable.");
        var session = new CaptureSession(device, registrations);
        device.OnPacketArrival += (_, capture) => Observe(session, capture.GetPacket());
        device.Open(DeviceModes.Promiscuous, 1_000);
        device.Filter = "ip or arp";
        device.StartCapture();
        return session;
    }

    private void Observe(CaptureSession session, RawCapture capture)
    {
        try
        {
            var data = capture.Data.AsSpan(0, Math.Min(capture.Data.Length,
                Math.Clamp(_config.ObservationSnapLength, 96, 65_535)));
            if (!PacketFingerprint.TryParse(capture.LinkLayerType, data, out var packet))
            {
                Interlocked.Increment(ref _parserFailures);
                return;
            }
            foreach (var registration in session.Registrations)
            {
                var sequence = spool.AppendPacket(registration, capture.Timeval.Date, packet);
                _flows.Observe(
                    $"{registration.PublicId:N}:{packet.FlowFingerprint}",
                    sequence,
                    capture.Timeval.Date,
                    packet.PacketLength);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Interlocked.Increment(ref _parserFailures);
            _lastError = Trim(exception.Message);
        }
    }

    private static string Trim(string value) => value.Length <= 512 ? value : value[..512];

    private sealed class CaptureSession(
        ICaptureDevice device,
        IReadOnlyList<ObservationPointRegistration> registrations) : IDisposable
    {
        private IReadOnlyList<ObservationPointRegistration> _registrations = registrations;
        public IReadOnlyList<ObservationPointRegistration> Registrations => Volatile.Read(ref _registrations);

        public void Update(IReadOnlyList<ObservationPointRegistration> registrations) =>
            Volatile.Write(ref _registrations, registrations);

        public void Dispose()
        {
            try
            {
                device.StopCapture();
                device.Close();
            }
            catch (PcapException)
            {
                // The interface may already have disappeared during generation cleanup.
            }
        }
    }
}
