using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Observation;
using GZCTF.GuestTelemetry.Contracts;
using GZCTF.GuestTelemetry.Platform;
using GZCTF.EndpointSensor.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PacketDotNet;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabObservationTests
{
    [Fact]
    public void PacketFingerprint_IsStableAcrossForwardingHeaderChanges()
    {
        var original = TcpFrame(ttl: 64, ipChecksum: 0x1234, tcpChecksum: 0x5678);
        var forwarded = TcpFrame(ttl: 63, ipChecksum: 0xabcd, tcpChecksum: 0xef01);

        Assert.True(PacketFingerprint.TryParse(LinkLayers.Ethernet, original, out var first));
        Assert.True(PacketFingerprint.TryParse(LinkLayers.Ethernet, forwarded, out var second));

        Assert.Equal(first.PacketFingerprint, second.PacketFingerprint);
        Assert.Equal(first.FlowFingerprint, second.FlowFingerprint);
        Assert.Equal("10.10.0.2", first.SourceIp);
        Assert.Equal(43122, first.SourcePort);
        Assert.Equal("192.168.50.10", first.DestinationIp);
        Assert.Equal(8080, first.DestinationPort);
    }

    [Fact]
    public void ObservationRegistry_ExpandsRouterFragmentToManagedHostInterfaces()
    {
        var runtimeId = Random.Shared.Next(10_000, 50_000);
        var request = new TeamLabInfrastructureApplyRequest(
            runtimeId,
            2,
            2,
            "tlr42",
            [
                new TeamLabManagedSwitchIntent("entry", "Entry", "10.0.0.0/24", "10.0.0.1", "tl-entry", "dns-entry", []),
                new TeamLabManagedSwitchIntent("core", "Core", "192.168.0.0/24", "192.168.0.1", "tl-core", "dns-core", [])
            ],
            [new TeamLabManagedRouterFragmentIntent("router", ["entry", "core"])],
            new TeamLabFabricUplinkIntent(
                "169.254.1.2", "169.254.1.1/30", "169.254.1.2/30", "tlf-host", "tlf-ns", [], []),
            [],
            [
                new TeamLabObservationPointIntent(Guid.Parse("11111111-1111-1111-1111-111111111111"), "entry", 0, "tl-entry"),
                new TeamLabObservationPointIntent(Guid.Parse("22222222-2222-2222-2222-222222222222"), "router", 1, "tlr42"),
                new TeamLabObservationPointIntent(Guid.Parse("33333333-3333-3333-3333-333333333333"), "fabric", 2, "tlf-host")
            ],
            false);

        var registrations = ObservationPointRegistry.Resolve(request);

        Assert.Equal(4, registrations.Length);
        Assert.Contains(registrations, item => item.InterfaceName == "tl-entry");
        Assert.Contains(registrations, item => item.InterfaceName == "tlr42h0");
        Assert.Contains(registrations, item => item.InterfaceName == "tlr42h1");
        Assert.Contains(registrations, item => item.InterfaceName == "tlf-host");
    }

    [Fact]
    public void ObservationSpool_AggregatesPacketsByFlowAndObservationPoint()
    {
        var options = Options.Create(new AgentTeamLabConfig
        {
            ObservationMemoryRecordLimit = 1_000,
            ObservationBatchSize = 100
        });
        var spool = new ObservationBatchSpool(options, NullLogger<ObservationBatchSpool>.Instance);
        var registration = new ObservationPointRegistration(
            71, 3, Guid.NewGuid(), "entry", 0, "tl-entry");
        var packet = new ParsedObservationPacket(
            "10.0.0.2", 1000, "10.0.0.3", 80, "TCP", 0x18, 64,
            "sha256:" + new string('a', 64), "sha256:" + new string('b', 64));
        var firstSeen = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        for (var index = 0; index < 1_005; index++)
            spool.AppendPacket(registration, firstSeen.AddMilliseconds(index), packet);

        var first = spool.Read(
            new TeamLabObservationBatchRequest(71, 3, 0, registration.PublicId, 100),
            Health());
        var second = spool.Read(
            new TeamLabObservationBatchRequest(71, 3, first.NextSequence, registration.PublicId, 100),
            Health());

        var aggregate = Assert.Single(first.Records);
        Assert.Equal(1_005, aggregate.Packets);
        Assert.Equal(1_005 * 64, aggregate.Bytes);
        Assert.Equal(firstSeen, aggregate.FirstSeenAt);
        Assert.Equal(firstSeen.AddMilliseconds(1_004), aggregate.LastSeenAt);
        Assert.Null(aggregate.PacketFingerprint);
        Assert.Empty(second.Records);
        Assert.Equal(first.NextSequence, second.NextSequence);

        var secondPoint = registration with { PublicId = Guid.NewGuid() };
        spool.AppendPacket(registration, firstSeen.AddSeconds(2), packet);
        spool.AppendPacket(secondPoint, firstSeen.AddSeconds(2), packet);
        var separated = spool.Read(new TeamLabObservationBatchRequest(71, 3, first.NextSequence), Health());
        Assert.Equal(2, separated.Records.Length);
        Assert.Equal(2, separated.Records.Select(item => item.ObservationPointId).Distinct().Count());

        spool.Remove(71, 3);
        Assert.Equal(0, spool.AppendPacket(registration, DateTimeOffset.UtcNow, packet));
        Assert.Empty(spool.Read(
            new TeamLabObservationBatchRequest(71, 3, 0, registration.PublicId, 100), Health()).Records);
        spool.Activate(71, 3);
        Assert.True(spool.AppendPacket(registration, DateTimeOffset.UtcNow, packet) > 0);
    }

    [Fact]
    public async Task ObservationSpool_RemoveWaitsForInFlightWriteAndPreventsRecreation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-observation-{Guid.NewGuid():N}");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = Options.Create(new AgentTeamLabConfig
        {
            ObservationMemoryRecordLimit = 1_000,
            ObservationBatchSize = 100,
            ObservationAggregationIntervalMilliseconds = 100
        });
        var spool = new ObservationBatchSpool(
            options,
            NullLogger<ObservationBatchSpool>.Instance,
            root,
            async cancellationToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            });
        var registration = new ObservationPointRegistration(
            72, 4, Guid.NewGuid(), "entry", 0, "tl-entry");
        var packet = new ParsedObservationPacket(
            "10.0.0.2", 1000, "10.0.0.3", 80, "TCP", 0x18, 64,
            "sha256:" + new string('a', 64), "sha256:" + new string('b', 64));

        try
        {
            await spool.StartAsync(CancellationToken.None);
            Assert.True(spool.AppendPacket(registration, DateTimeOffset.UtcNow, packet) > 0);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var remove = Task.Run(() => spool.Remove(72, 4));
            await Task.Delay(50);
            Assert.False(remove.IsCompleted);

            release.TrySetResult();
            await remove.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);

            Assert.False(File.Exists(Path.Combine(root, "runtime-72", "generation-4", "records.jsonl")));
        }
        finally
        {
            release.TrySetResult();
            await spool.StopAsync(CancellationToken.None);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ObservationSpool_PreservesExplicitPacketFingerprintsWhenEnabled()
    {
        var options = Options.Create(new AgentTeamLabConfig
        {
            ObservationPacketFingerprintEnabled = true
        });
        var spool = new ObservationBatchSpool(options, NullLogger<ObservationBatchSpool>.Instance);
        var registration = new ObservationPointRegistration(
            73, 5, Guid.NewGuid(), "entry", 0, "tl-entry");
        var packet = new ParsedObservationPacket(
            "10.0.0.2", 1000, "10.0.0.3", 80, "TCP", 0x18, 64,
            "sha256:" + new string('a', 64), "sha256:" + new string('b', 64));

        spool.AppendPacket(registration, DateTimeOffset.UtcNow, packet);
        spool.AppendPacket(registration, DateTimeOffset.UtcNow, packet);

        var records = spool.Read(new TeamLabObservationBatchRequest(73, 5), Health()).Records;
        Assert.Equal(2, records.Length);
        Assert.All(records, item =>
        {
            Assert.Equal(packet.PacketFingerprint, item.PacketFingerprint);
            Assert.Equal(1, item.Packets);
            Assert.Equal(packet.PacketLength, item.Bytes);
        });
    }

    [Fact]
    public async Task ObservationSpool_AcknowledgementReleasesOnlyPersistedRecords()
    {
        var spool = new ObservationBatchSpool(
            Options.Create(new AgentTeamLabConfig { ObservationPacketFingerprintEnabled = true }),
            NullLogger<ObservationBatchSpool>.Instance);
        var registration = new ObservationPointRegistration(74, 1, Guid.NewGuid(), "entry", 0, "tl-entry");
        var packet = new ParsedObservationPacket(
            "10.0.0.2", 1000, "10.0.0.3", 80, "TCP", 0x18, 64,
            "sha256:" + new string('a', 64), "sha256:" + new string('b', 64));

        var first = spool.AppendPacket(registration, DateTimeOffset.UtcNow, packet);
        var second = spool.AppendPacket(registration, DateTimeOffset.UtcNow, packet);
        await spool.AcknowledgeAsync(74, 1, first, CancellationToken.None);

        var remaining = spool.Read(new TeamLabObservationBatchRequest(74, 1), Health()).Records;
        var record = Assert.Single(remaining);
        Assert.Equal(second, record.Sequence);
    }

    [Fact]
    public void ObservationSpool_DoesNotCountWriterBacklogAsDroppedTraffic()
    {
        const int recordCount = 33_000;
        var spool = new ObservationBatchSpool(
            Options.Create(new AgentTeamLabConfig
            {
                ObservationPacketFingerprintEnabled = true,
                ObservationMemoryRecordLimit = 1_000,
                ObservationBatchSize = 2_000,
                ObservationSpoolMaxBytes = 64L * 1024 * 1024
            }),
            NullLogger<ObservationBatchSpool>.Instance);
        var registration = new ObservationPointRegistration(75, 1, Guid.NewGuid(), "entry", 0, "tl-entry");
        var packet = new ParsedObservationPacket(
            "10.0.0.2", 1000, "10.0.0.3", 80, "TCP", 0x18, 64,
            "sha256:" + new string('a', 64), "sha256:" + new string('b', 64));

        for (var index = 0; index < recordCount; index++)
            Assert.True(spool.AppendPacket(registration, DateTimeOffset.UtcNow, packet) > 0);

        var first = spool.Read(new TeamLabObservationBatchRequest(75, 1, 0, registration.PublicId, 2_000), Health());
        Assert.Equal(0, first.DroppedCount);
        Assert.Equal(2_000, first.Records.Length);
        Assert.Equal(2_000, first.NextSequence);
    }

    [Fact]
    public void EndpointSensorSignature_IsAcceptedOnceAndRejectsReplay()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var runtimePublicId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow;
        var signed = new SensorEventSigner(key).Sign(new SensorEvent(
            1,
            runtimePublicId.ToString("D"),
            4,
            "windows-ad",
            100,
            observedAt,
            SensorEventKind.Opened,
            new SensorProcessIdentity(4242, "lsass", observedAt.AddMinutes(-2)),
            new SensorEndpoint("192.168.10.10", 49152, "TCP"),
            new SensorEndpoint("192.168.10.20", 389, "TCP"),
            string.Empty));
        var agentEvent = new EndpointSensorEvent(
            signed.SchemaVersion,
            signed.RuntimePublicId,
            signed.Generation,
            signed.AssetKey,
            signed.Sequence,
            signed.ObservedAt,
            (EndpointSensorEventKind)signed.Kind,
            new EndpointSensorProcessIdentity(
                signed.Process.ProcessId, signed.Process.Name, signed.Process.StartedAt),
            new EndpointSensorEndpoint(signed.Local.Address, signed.Local.Port, signed.Local.Protocol),
            new EndpointSensorEndpoint(signed.Remote.Address, signed.Remote.Port, signed.Remote.Protocol),
            signed.Signature);

        var accepted = EndpointSensorAuthenticator.Verify(
            agentEvent, runtimePublicId, 4, "windows-ad", 99, key, observedAt);
        var replayed = EndpointSensorAuthenticator.Verify(
            agentEvent, runtimePublicId, 4, "windows-ad", 100, key, observedAt);

        Assert.True(accepted.Success, accepted.Code);
        Assert.NotNull(accepted.ProcessIdentityHash);
        Assert.False(replayed.Success);
        Assert.Equal("sensor_sequence_replayed", replayed.Code);
    }

    [Fact]
    public void LinuxConnectionProvider_ParsesProcIpv4Endpoint()
    {
        Assert.True(LinuxConnectionProvider.TryEndpoint("02000A0A:A872", out var address, out var port));
        Assert.Equal("10.10.0.2", address);
        Assert.Equal(43122, port);
    }

    [Fact]
    public void FlowAccumulator_EvictsDeterministicallyAtCapacity()
    {
        var accumulator = new FlowAccumulator(128);
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 129; index++)
            accumulator.Observe($"flow-{index:D3}", index + 1, now, 64);

        var snapshot = accumulator.Snapshot();
        Assert.Equal(128, snapshot.ActiveCount);
        Assert.Equal(1, snapshot.EvictedCount);
    }

    private static TeamLabObservationHealth Health() =>
        new(true, 1, 1, 1, 0, 0, 0, 0, null, null);

    private static byte[] TcpFrame(byte ttl, ushort ipChecksum, ushort tcpChecksum)
    {
        var frame = new byte[14 + 20 + 20 + 4];
        frame[12] = 0x08;
        frame[13] = 0x00;
        var ip = frame.AsSpan(14);
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip[2..4], 44);
        BinaryPrimitives.WriteUInt16BigEndian(ip[4..6], 0x2345);
        BinaryPrimitives.WriteUInt16BigEndian(ip[6..8], 0x4000);
        ip[8] = ttl;
        ip[9] = 6;
        BinaryPrimitives.WriteUInt16BigEndian(ip[10..12], ipChecksum);
        IPAddress.Parse("10.10.0.2").GetAddressBytes().CopyTo(ip[12..16]);
        IPAddress.Parse("192.168.50.10").GetAddressBytes().CopyTo(ip[16..20]);
        var tcp = ip[20..];
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], 43122);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[2..4], 8080);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[4..8], 0x01020304);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[8..12], 0x05060708);
        tcp[12] = 0x50;
        tcp[13] = 0x18;
        BinaryPrimitives.WriteUInt16BigEndian(tcp[14..16], 65535);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[16..18], tcpChecksum);
        tcp[20] = 1;
        tcp[21] = 2;
        tcp[22] = 3;
        tcp[23] = 4;
        return frame;
    }
}
