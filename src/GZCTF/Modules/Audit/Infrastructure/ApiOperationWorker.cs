using GZCTF.Modules.Audit.Application;

namespace GZCTF.Modules.Audit.Infrastructure;

public sealed class ApiOperationWorker : BackgroundService
{
    private const int BatchSize = 8;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeaseRenewInterval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiOperationWorker> _logger;
    private readonly string _leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public ApiOperationWorker(IServiceScopeFactory scopeFactory, ILogger<ApiOperationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var active = new HashSet<Task>();
        _logger.LogInformation("External API operation worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            active.RemoveWhere(task => task.IsCompleted);
            IReadOnlyList<Domain.ApiOperation> operations;
            try
            {
                var availableSlots = BatchSize - active.Count;
                if (availableSlots > 0)
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<ApiOperationService>();
                    operations = await service.ClaimAsync(
                        _leaseOwner,
                        LeaseDuration,
                        availableSlots,
                        stoppingToken);
                }
                else
                {
                    operations = [];
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to claim external API operations");
                await DelayAsync(stoppingToken);
                continue;
            }

            foreach (var operation in operations)
                active.Add(ProcessSafelyAsync(operation, stoppingToken));

            if (active.Count == 0)
            {
                await DelayAsync(stoppingToken);
                continue;
            }

            if (operations.Count > 0 && active.Count < BatchSize)
                continue;

            var poll = Task.Delay(PollInterval, stoppingToken);
            await Task.WhenAny(active.Append(poll));
        }

        await Task.WhenAll(active);
    }

    private async Task ProcessSafelyAsync(
        Domain.ApiOperation operation,
        CancellationToken stoppingToken)
    {
        try
        {
            await ProcessAsync(operation, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Operation {OperationId} processing infrastructure failed; the lease will be recovered",
                operation.Id);
        }
    }

    private async Task ProcessAsync(
        Domain.ApiOperation operation,
        CancellationToken stoppingToken)
    {
        await using var handlerScope = _scopeFactory.CreateAsyncScope();
        var handler = handlerScope.ServiceProvider.GetKeyedService<IApiOperationHandler>(operation.Kind);
        if (handler is null)
        {
            await MarkFailedAsync(
                operation.Id,
                "operation_handler_missing",
                $"No operation handler is registered for kind '{operation.Kind}'.",
                0,
                stoppingToken);
            return;
        }

        if (!string.Equals(handler.Kind, operation.Kind, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Operation handler registration '{operation.Kind}' resolved handler kind '{handler.Kind}'.");
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewal = RenewLeaseAsync(operation.Id, execution, stoppingToken);
        try
        {
            await handler.ExecuteAsync(operation.Id, _leaseOwner, execution.Token);
            if (execution.IsCancellationRequested)
                return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ApiOperationService>();
            if (!await service.CompleteAsync(
                    operation.Id, _leaseOwner, null, null, stoppingToken))
                _logger.LogWarning(
                    "Operation {OperationId} completed after its lease was lost", operation.Id);
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
            // A shutdown or lost lease leaves the operation recoverable after lease expiry.
        }
        catch (ApiOperationDeferredException exception)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ApiOperationService>();
            if (!await service.DeferAsync(
                    operation.Id,
                    _leaseOwner,
                    exception.Stage,
                    exception.Code,
                    exception.Message,
                    exception.Delay,
                    stoppingToken))
                _logger.LogWarning("Operation {OperationId} lost its lease while deferring", operation.Id);
        }
        catch (ApiOperationTerminalException exception)
        {
            _logger.LogWarning(
                "External API operation {OperationId} was rejected: {ErrorCode}",
                operation.Id,
                exception.Code);
            if (await MarkFailedAsync(
                operation.Id,
                exception.Code,
                exception.Message,
                0,
                stoppingToken))
                await handler.OnTerminalFailureAsync(operation.Id, stoppingToken);
        }
        catch (ApiContractException exception) when (exception.StatusCode < 500)
        {
            _logger.LogWarning(
                "External API operation {OperationId} was rejected: {ErrorCode}",
                operation.Id,
                exception.Code);
            if (await MarkFailedAsync(
                    operation.Id,
                    exception.Code,
                    exception.Message,
                    0,
                    stoppingToken))
                await handler.OnTerminalFailureAsync(operation.Id, stoppingToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "External API operation {OperationId} failed", operation.Id);
            var retryDelay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, operation.AttemptCount)));
            var reachedTerminalState = operation.AttemptCount >= MaxAttempts;
            var persisted = await MarkFailedAsync(
                operation.Id,
                "operation_failed",
                "The operation failed. Retry or contact an administrator with the operation ID.",
                MaxAttempts,
                stoppingToken,
                retryDelay);
            if (reachedTerminalState && persisted)
                await handler.OnTerminalFailureAsync(operation.Id, stoppingToken);
        }
        finally
        {
            await execution.CancelAsync();
            try
            {
                await renewal;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Lease renewal failed for operation {OperationId}", operation.Id);
            }
        }
    }

    private async Task RenewLeaseAsync(
        Guid operationId,
        CancellationTokenSource execution,
        CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(LeaseRenewInterval);
            while (await timer.WaitForNextTickAsync(execution.Token))
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ApiOperationService>();
                if (await service.RenewLeaseAsync(
                        operationId,
                        _leaseOwner,
                        LeaseDuration,
                        stoppingToken))
                    continue;

                _logger.LogWarning("Operation {OperationId} lost its execution lease", operationId);
                await execution.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested) { }
        catch
        {
            await execution.CancelAsync();
            throw;
        }
    }

    private async Task<bool> MarkFailedAsync(
        Guid operationId,
        string errorCode,
        string errorDetail,
        int maxAttempts,
        CancellationToken cancellationToken,
        TimeSpan? retryDelay = null)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ApiOperationService>();
        return await service.RetryOrFailAsync(
            operationId,
            _leaseOwner,
            maxAttempts,
            errorCode,
            errorDetail,
            retryDelay ?? TimeSpan.Zero,
            cancellationToken);
    }

    private static async Task DelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PollInterval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
