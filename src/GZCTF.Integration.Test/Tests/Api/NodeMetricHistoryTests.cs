using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class NodeMetricHistoryTests(GZCTFApplicationFactory factory)
{
    private const string Endpoint = "/api/v1/nodes/metrics/history";
    private static readonly DateTimeOffset Now = new(2032, 1, 2, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task History_ReadsPersistedOfflineNodesAndRetainsMissingBuckets()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = AddNode(db);
        var second = AddNode(db);
        var time = Now.AddMinutes(-24).AddSeconds(-56); // 12:10 UTC
        db.WorkerNodeMetricSamples.AddRange(
            Sample(first.Id, time, .2f, .3f, 1),
            Sample(first.Id, time.AddMinutes(1), .4f, .5f, 9),
            Sample(second.Id, time, .8f, .7f, 2));
        await db.SaveChangesAsync();
        try
        {
            var query = new NodeMetricHistoryQuery(db, new FixedClock());
            var history = Assert.IsType<NodeMetricHistory>(await query.ReadAsync("24h", null, default));
            Assert.Equal(288, history.Points.Length);
            Assert.Equal(300, history.IntervalSeconds);
            var point = Assert.Single(history.Points.Where(point => point.SampleCount > 0));
            Assert.Equal(time, point.Timestamp);
            Assert.Equal((.2 + .4 + .8) / 3, point.CpuLoad!.Value, 5);
            Assert.Equal(.5, point.MemoryLoad!.Value, 5);
            Assert.Equal(12, point.SampleCount);
            Assert.Equal(2, point.NodeCount);
            Assert.All(history.Points.Where(point => point.SampleCount == 0), point =>
            {
                Assert.Null(point.CpuLoad);
                Assert.Null(point.MemoryLoad);
                Assert.Equal(0, point.NodeCount);
            });
            // A new query (as after navigation/reload) must retrieve the same historic samples.
            var reopened = await new NodeMetricHistoryQuery(db, new FixedClock()).ReadAsync("24h", null, default);
            Assert.Equal(history.Points, reopened!.Points);
        }
        finally { db.WorkerNodes.RemoveRange(first, second); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task History_FiltersNodeAndIgnoresInvalidValuesIndependently()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = AddNode(db);
        var second = AddNode(db);
        var time = Now.AddMinutes(-4).AddSeconds(-56);
        db.WorkerNodeMetricSamples.AddRange(
            Sample(first.Id, time, float.NaN, .2f),
            Sample(first.Id, time.AddMinutes(1), 1.2f, float.PositiveInfinity),
            Sample(second.Id, time, .9f, .8f),
            Sample(first.Id, time.AddDays(-8), .4f, .4f));
        await db.SaveChangesAsync();
        try
        {
            var query = new NodeMetricHistoryQuery(db, new FixedClock());
            var history = await query.ReadAsync("24h", first.Id, default);
            var point = Assert.Single(history!.Points.Where(point => point.SampleCount > 0));
            Assert.Null(point.CpuLoad);
            Assert.Equal(.2, point.MemoryLoad!.Value, 5);
            Assert.Equal(1, point.NodeCount);
            Assert.Equal(2, point.SampleCount);
            Assert.Null(await query.ReadAsync("24h", Guid.NewGuid(), default));
        }
        finally { db.WorkerNodes.RemoveRange(first, second); await db.SaveChangesAsync(); }
    }

    [Theory]
    [InlineData("1h", 60, 60)]
    [InlineData("24h", 300, 288)]
    [InlineData("7d", 1800, 336)]
    public async Task History_BoundsTheResponseAndKeepsUtcBuckets(string range, int interval, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var node = AddNode(db);
        await db.SaveChangesAsync();
        try
        {
            var history = await new NodeMetricHistoryQuery(db, new FixedClock()).ReadAsync(range, node.Id, default);
            Assert.Equal(interval, history!.IntervalSeconds);
            Assert.Equal(count, history.Points.Length);
            Assert.Equal(history.FromUtc, history.Points[0].Timestamp);
            Assert.Equal(history.ToUtc.AddSeconds(-interval), history.Points[^1].Timestamp);
            Assert.All(history.Points, point =>
            {
                Assert.Equal(0, point.Timestamp.ToUnixTimeSeconds() % interval);
                Assert.Null(point.CpuLoad);
                Assert.Null(point.MemoryLoad);
            });
        }
        finally { db.WorkerNodes.Remove(node); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task History_RequiresAdministratorAndValidatesQuery()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Endpoint)).StatusCode);
        using var teacher = await Login(Role.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Endpoint)).StatusCode);
        using var admin = await Login(Role.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(Endpoint + "?range=30d")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(Endpoint + "?nodeId=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(Endpoint + "?nodeId=" + Guid.NewGuid())).StatusCode);
        using var response = await admin.GetAsync(Endpoint + "?range=1h");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("fromUtc").ValueKind);
        Assert.Equal(60, json.RootElement.GetProperty("points").GetArrayLength());
    }

    private static WorkerNode AddNode(AppDbContext db)
    {
        var node = new WorkerNode { Name = TestDataSeeder.RandomName(), HostAddress = "127.0.0.1", Status = NodeStatus.Offline };
        db.WorkerNodes.Add(node);
        return node;
    }

    private static WorkerNodeMetricSample Sample(Guid nodeId, DateTimeOffset time,
        float cpu, float memory, int count = 1) => new()
    {
        WorkerNodeId = nodeId, WindowStart = time, SampleCount = count,
        AverageCpuLoad = cpu, AverageMemoryLoad = memory,
        FirstReceivedAt = time, LastReceivedAt = time.AddSeconds(10)
    };

    private async Task<HttpClient> Login(Role role)
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), "Metrics@Test123", role: role);
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = user.Password })).EnsureSuccessStatusCode();
        return client;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
