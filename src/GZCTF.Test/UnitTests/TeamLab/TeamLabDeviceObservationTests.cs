using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabDeviceObservationTests
{
    [Fact]
    public async Task PersistsCounterBaselineAcrossFailuresAndHonorsPowerIntent()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var runtime = new TeamLabRuntime { Id = 1, Status = TeamLabRuntimeStatus.Running };
        var node = Guid.NewGuid();
        var asset = new TeamLabRuntimeAsset { Id = 1, Runtime = runtime, RuntimeId = 1, Generation = 1, ShardId = 1,
            WorkerNodeId = node, TopologyKey = "plc", Name = "PLC", RuntimeResourceId = "container", DevicePackageId = 7 };
        var digest = "sha256:" + new string('a', 64);
        var device = new TeamLabDeviceExecutionV2(Guid.NewGuid(), "plc", "1.0", digest, "{}", "http", 1503, "/health", 1, ["modbus.read"]);
        var plan = new TeamLabExecutionPlanV2(1, runtime.PublicId, 1, "1", "", digest, true, [],
            [new("plc", "docker", "container", digest, null, 1, 1, 64, [], [new("http", "127.0.0.1", 1503, "/health")], Device: device)], []);
        plan = plan with { PlanDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan))) };
        db.TeamLabRuntimeAssets.Add(asset);
        db.TeamLabExecutionPlanSnapshots.Add(new() { Runtime = runtime, RuntimeId = 1, Generation = 1, ShardId = 1, WorkerNodeId = node, PlanJson = JsonSerializer.Serialize(plan) });
        await db.SaveChangesAsync();
        var observer = new Observer();
        var service = new TeamLabDeviceObservationService(db, observer,
            new TeamLabEventRecorder(db, new EfOperationalEventWriter(db, NullLogger<EfOperationalEventWriter>.Instance), new OperationalCorrelation()),
            new TeamLabAuthorizationService(db, [], []));
        observer.Value = Healthy(10);
        await service.ObserveAsync(1, default);
        await service.ObserveAsync(1, default);
        Assert.Single(db.TeamLabEvents.Where(item => item.Stage == "protocol"));
        observer.Value = new("unavailable", DateTimeOffset.UtcNow, "device.node_unavailable");
        await service.ObserveAsync(1, default);
        observer.Value = Healthy(12);
        await service.ObserveAsync(1, default);
        Assert.Contains(db.TeamLabEvents, item => item.Stage == "protocol" && item.Message.Contains("新增 2 次"));
        observer.Value = Healthy(1);
        await service.ObserveAsync(1, default);
        Assert.Contains("device.counter_regressed", asset.DeviceObservationJson);
        var count = observer.Count;
        asset.DesiredPowerState = "paused";
        await db.SaveChangesAsync();
        await service.ObserveAsync(1, default);
        Assert.Equal(count, observer.Count);
        Assert.Contains("stopped", asset.DeviceObservationJson);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"bootId\":\"bad\",\"counters\":{\"modbus.read\":1}}")]
    [InlineData("{\"bootId\":\"01900000-0000-7000-8000-000000000001\",\"counters\":{\"modbus.read\":-1}}")]
    public void RejectsMalformedTelemetry(string body) => Assert.Equal("device.telemetry_invalid",
        TeamLabExecutionPlanExecutor.ParseProtocolCounters(body, ["modbus.read"], DateTimeOffset.UtcNow).ErrorCode);

    static TeamLabDeviceObservation Healthy(long value) => new("healthy", DateTimeOffset.UtcNow,
        BootId: "01900000-0000-7000-8000-000000000001", ProtocolCounters: new Dictionary<string, long> { ["modbus.read"] = value });
    sealed class Observer : ITeamLabDeviceObserver
    {
        public TeamLabDeviceObservation Value { get; set; } = Healthy(0);
        public int Count { get; private set; }
        public Task<TeamLabDeviceObservation?> ProbeAsync(Guid node, TeamLabDeviceProbeRequest request, CancellationToken token)
        { Count++; return Task.FromResult<TeamLabDeviceObservation?>(Value); }
    }
}
