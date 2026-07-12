using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Cache;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace GZCTF.Test.UnitTests.Cache;

public class RedisRuntimeInfrastructureTests
{
    [Fact]
    public void OptionsValidator_EnforcesDistributedAndProductionBoundaries()
    {
        var distributed = ValidOptions();
        distributed.Mode = RedisRuntimeMode.Distributed;
        distributed.ConnectionString = null;

        var missingConnection = RedisRuntimeOptionsValidator.Validate(distributed, isProduction: true);
        Assert.True(missingConnection.Failed);
        Assert.Contains(missingConnection.Failures,
            failure => failure.Contains("connection string", StringComparison.OrdinalIgnoreCase));

        var singleInstance = ValidOptions();
        singleInstance.Mode = RedisRuntimeMode.SingleInstance;
        singleInstance.ApplicationInstanceCount = 2;

        var unsafeProductionMode = RedisRuntimeOptionsValidator.Validate(singleInstance, isProduction: true);
        Assert.True(unsafeProductionMode.Failed);
        Assert.Contains(unsafeProductionMode.Failures,
            failure => failure.Contains("multi-instance", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("GZCTF")]
    [InlineData("gzctf cache")]
    [InlineData("gzctf{cache}")]
    public void OptionsValidator_RejectsNonCanonicalKeyPrefix(string prefix)
    {
        var options = ValidOptions();
        options.KeyPrefix = prefix;

        var result = RedisRuntimeOptionsValidator.Validate(options, isProduction: false);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Keyspace_ProducesVersionedPurposeAndHashTagKeys()
    {
        var keyspace = new RedisKeyspace("gzctf");

        var key = keyspace.CreateTagged(RedisKeyPurpose.Lease, "port", "public", "30042");

        Assert.Equal("gzctf:v1:lease:port:{public}:30042", key.ToString());
    }

    [Fact]
    public void Keyspace_HashesSensitiveResourcesAndRejectsPlaintextIpSegments()
    {
        const string token = "team-token-with-sensitive-value";
        var keyspace = new RedisKeyspace("gzctf");

        var key = keyspace.CreateOpaque(RedisKeyPurpose.Cache, "team", token).ToString();

        Assert.StartsWith("gzctf:v1:cache:team:sha256:", key, StringComparison.Ordinal);
        Assert.DoesNotContain(token, key, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            keyspace.Create(RedisKeyPurpose.Cache, "node", "10.24.0.31"));
    }

    [Fact]
    public async Task ConnectionProvider_DisabledModeNeverCreatesAConnection()
    {
        var options = ValidOptions();
        options.Mode = RedisRuntimeMode.Disabled;
        var calls = 0;
        using var telemetry = new RedisTelemetry();
        var state = new RedisRuntimeState(Options.Create(options), telemetry);
        await using var provider = new RedisConnectionProvider(Options.Create(options), state, telemetry,
            NullLogger<RedisConnectionProvider>.Instance, _ =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("Connection factory must not be called.");
            });

        var connection = await provider.GetAsync();

        Assert.Null(connection);
        Assert.False(provider.IsConfigured);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task ConnectionProvider_ConcurrentCallersShareOneAsyncInitialization()
    {
        var options = ValidOptions();
        options.Mode = RedisRuntimeMode.Distributed;
        options.ConnectionString = "localhost:6379";
        var connection = new Mock<IConnectionMultiplexer>();
        connection.SetupGet(item => item.IsConnected).Returns(true);
        connection.Setup(item => item.CloseAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);
        var calls = 0;
        using var telemetry = new RedisTelemetry();
        var state = new RedisRuntimeState(Options.Create(options), telemetry);
        await using var provider = new RedisConnectionProvider(Options.Create(options), state, telemetry,
            NullLogger<RedisConnectionProvider>.Instance, async configuration =>
            {
                Interlocked.Increment(ref calls);
                Assert.False(configuration.AbortOnConnectFail);
                Assert.Equal(options.ClientName, configuration.ClientName);
                await Task.Delay(20);
                return connection.Object;
            });

        var results = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(async _ => await provider.GetAsync()));

        Assert.All(results, result => Assert.Same(connection.Object, result));
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal(RedisRuntimeStatus.Healthy,
            state.Get(RedisRuntimeComponent.Connection).Status);
    }

    [Fact]
    public async Task HealthCheck_DistributedDisconnectedIsUnhealthy()
    {
        var options = ValidOptions();
        options.Mode = RedisRuntimeMode.Distributed;
        options.ConnectionString = "localhost:6379";
        var connection = new Mock<IConnectionMultiplexer>();
        connection.SetupGet(item => item.IsConnected).Returns(false);
        connection.Setup(item => item.CloseAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);
        using var telemetry = new RedisTelemetry();
        var state = new RedisRuntimeState(Options.Create(options), telemetry);
        await using var provider = new RedisConnectionProvider(Options.Create(options), state, telemetry,
            NullLogger<RedisConnectionProvider>.Instance, _ => Task.FromResult(connection.Object));
        var healthCheck = new RedisHealthCheck(provider, state, Options.Create(options));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(false, result.Data["connected"]);
    }

    [Fact]
    public async Task HealthCheck_SingleInstanceWithoutRedisIsHealthy()
    {
        var options = ValidOptions();
        options.Mode = RedisRuntimeMode.SingleInstance;
        options.ConnectionString = null;
        using var telemetry = new RedisTelemetry();
        var state = new RedisRuntimeState(Options.Create(options), telemetry);
        await using var provider = new RedisConnectionProvider(Options.Create(options), state, telemetry,
            NullLogger<RedisConnectionProvider>.Instance, _ =>
                throw new InvalidOperationException("Connection factory must not be called."));
        var healthCheck = new RedisHealthCheck(provider, state, Options.Create(options));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static RedisRuntimeOptions ValidOptions() => new()
    {
        KeyPrefix = "gzctf",
        ClientName = "gzctf-test",
        ConnectTimeout = TimeSpan.FromSeconds(2),
        OperationTimeout = TimeSpan.FromSeconds(2),
        StreamLagWarningThreshold = TimeSpan.FromSeconds(2),
        ApplicationInstanceCount = 1
    };
}
