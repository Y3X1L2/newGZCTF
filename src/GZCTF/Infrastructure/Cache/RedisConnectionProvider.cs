using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GZCTF.Infrastructure.Cache;

public interface IRedisConnectionProvider
{
    bool IsConfigured { get; }
    RedisRuntimeMode Mode { get; }
    ValueTask<IConnectionMultiplexer?> GetAsync(CancellationToken token = default);
}

public sealed class RedisConnectionProvider : IRedisConnectionProvider, IAsyncDisposable
{
    private readonly RedisRuntimeOptions _options;
    private readonly RedisRuntimeState _runtimeState;
    private readonly RedisTelemetry _telemetry;
    private readonly ILogger<RedisConnectionProvider> _logger;
    private readonly Func<ConfigurationOptions, Task<IConnectionMultiplexer>> _connect;
    private readonly Lazy<Task<IConnectionMultiplexer?>> _connection;
    private IConnectionMultiplexer? _attachedConnection;
    private int _disposed;

    public RedisConnectionProvider(IOptions<RedisRuntimeOptions> options, RedisRuntimeState runtimeState,
        RedisTelemetry telemetry, ILogger<RedisConnectionProvider> logger)
        : this(options, runtimeState, telemetry, logger, ConnectAsync)
    {
    }

    internal RedisConnectionProvider(IOptions<RedisRuntimeOptions> options, RedisRuntimeState runtimeState,
        RedisTelemetry telemetry, ILogger<RedisConnectionProvider> logger,
        Func<ConfigurationOptions, Task<IConnectionMultiplexer>> connect)
    {
        _options = options.Value;
        var validation = RedisRuntimeOptionsValidator.Validate(_options, false);
        if (validation.Failed)
            throw new OptionsValidationException(RedisRuntimeOptions.SectionName, typeof(RedisRuntimeOptions),
                validation.Failures);

        _runtimeState = runtimeState;
        _telemetry = telemetry;
        _logger = logger;
        _connect = connect;
        _connection = new(ConnectCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsConfigured => _options.Mode != RedisRuntimeMode.Disabled &&
                                !string.IsNullOrWhiteSpace(_options.ConnectionString);

    public RedisRuntimeMode Mode => _options.Mode;

    public async ValueTask<IConnectionMultiplexer?> GetAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return await _connection.Value.WaitAsync(token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || !_connection.IsValueCreated)
            return;

        IConnectionMultiplexer? connection;
        try
        {
            connection = await _connection.Value.ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (connection is null)
            return;

        Detach(connection);
        try
        {
            await connection.CloseAsync(allowCommandsToComplete: true).ConfigureAwait(false);
        }
        finally
        {
            connection.Dispose();
        }
    }

    private async Task<IConnectionMultiplexer?> ConnectCoreAsync()
    {
        if (!IsConfigured)
            return null;

        var configuration = ConfigurationOptions.Parse(_options.ConnectionString!, ignoreUnknown: false);
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = checked((int)_options.ConnectTimeout.TotalMilliseconds);
        configuration.AsyncTimeout = checked((int)_options.OperationTimeout.TotalMilliseconds);
        configuration.SyncTimeout = checked((int)_options.OperationTimeout.TotalMilliseconds);
        configuration.ClientName = _options.ClientName;

        try
        {
            var connection = await _connect(configuration).ConfigureAwait(false);
            Attach(connection);
            if (connection.IsConnected)
                _runtimeState.MarkConnectionAvailable();
            else
                _runtimeState.MarkConnectionUnavailable("initial-connect-unavailable");
            return connection;
        }
        catch (Exception exception)
        {
            _runtimeState.MarkConnectionUnavailable("initial-connect-failed");
            _logger.LogError(exception, "Redis connection initialization failed in {Mode} mode", _options.Mode);
            throw;
        }
    }

    private void Attach(IConnectionMultiplexer connection)
    {
        _attachedConnection = connection;
        connection.ConnectionFailed += OnConnectionFailed;
        connection.ConnectionRestored += OnConnectionRestored;
        connection.InternalError += OnInternalError;
    }

    private void Detach(IConnectionMultiplexer connection)
    {
        connection.ConnectionFailed -= OnConnectionFailed;
        connection.ConnectionRestored -= OnConnectionRestored;
        connection.InternalError -= OnInternalError;
        if (ReferenceEquals(_attachedConnection, connection))
            _attachedConnection = null;
    }

    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs args)
    {
        _runtimeState.MarkConnectionUnavailable("connection-failed");
        _logger.LogWarning(args.Exception, "Redis connection failed on {EndPoint} ({FailureType})",
            args.EndPoint, args.FailureType);
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs args)
    {
        _runtimeState.MarkConnectionAvailable(reconnected: true);
        _logger.LogInformation("Redis connection restored on {EndPoint}", args.EndPoint);
    }

    private void OnInternalError(object? sender, InternalErrorEventArgs args)
    {
        _telemetry.RecordOperation(RedisTelemetryPurpose.Connection, RedisTelemetryStatus.Failure);
        _logger.LogWarning(args.Exception, "Redis internal error at {Origin}", args.Origin);
    }

    private static async Task<IConnectionMultiplexer> ConnectAsync(ConfigurationOptions configuration) =>
        await ConnectionMultiplexer.ConnectAsync(configuration).ConfigureAwait(false);
}
