using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public sealed class ApiOperationWorkerTests
{
    [Fact]
    public async Task RenewalInfrastructureFailure_CancelsHandlerWithoutStoppingWorker()
    {
        var operation = new ApiOperation
        {
            Kind = BlockingHandler.OperationKind,
            Status = ApiOperationStatus.Running,
            Stage = "running",
            ApiTokenId = Guid.CreateVersion7(),
            RouteKey = BlockingHandler.OperationKind,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = "request-hash",
            AttemptCount = 1
        };
        var claimCount = 0;
        var store = new Mock<IApiOperationStore>();
        store.Setup(item => item.ClaimAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claimCount) == 1
                ? new[] { operation }
                : Array.Empty<ApiOperation>());
        store.Setup(item => item.RenewLeaseAsync(
                operation.Id,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lease backend unavailable"));

        var state = new BlockingHandlerState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(store.Object);
        services.AddScoped<ApiOperationService>();
        services.AddSingleton(state);
        services.AddScoped<IApiOperationHandler, BlockingHandler>();
        await using var provider = services.BuildServiceProvider();
        var worker = new ApiOperationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApiOperationWorker>.Instance);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await state.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(13));

            Assert.False(worker.ExecuteTask?.IsCompleted ?? true);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await worker.StopAsync(stopTimeout.Token);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task TerminalFailure_NotifiesHandlerForDurableCleanup()
    {
        var operation = new ApiOperation
        {
            Kind = TerminalFailureHandler.OperationKind,
            Status = ApiOperationStatus.Running,
            Stage = "running",
            ApiTokenId = Guid.CreateVersion7(),
            RouteKey = TerminalFailureHandler.OperationKind,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = "request-hash",
            AttemptCount = 5
        };
        var claimCount = 0;
        var state = new TerminalFailureState();
        var store = new Mock<IApiOperationStore>();
        store.Setup(item => item.ClaimAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claimCount) == 1
                ? new[] { operation }
                : Array.Empty<ApiOperation>());
        store.Setup(item => item.RetryOrFailAsync(
                operation.Id,
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => state.FailurePersisted.TrySetResult())
            .ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(store.Object);
        services.AddScoped<ApiOperationService>();
        services.AddSingleton(state);
        services.AddScoped<IApiOperationHandler, TerminalFailureHandler>();
        await using var provider = services.BuildServiceProvider();
        var worker = new ApiOperationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApiOperationWorker>.Instance);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await state.TerminalFailureNotified.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(state.FailurePersisted.Task.IsCompleted);
            Assert.Equal(operation.Id, state.OperationId);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await worker.StopAsync(stopTimeout.Token);
            worker.Dispose();
        }
    }

    [Fact]
    public void LeaseStoreContract_DoesNotAcceptAbsoluteHostTimestamps()
    {
        var leaseMethods = new[]
        {
            nameof(IApiOperationStore.ClaimAsync),
            nameof(IApiOperationStore.RenewLeaseAsync),
            nameof(IApiOperationStore.CompleteAsync),
            nameof(IApiOperationStore.UpdateProgressAsync),
            nameof(IApiOperationStore.RetryOrFailAsync)
        };

        foreach (var methodName in leaseMethods)
        {
            var method = typeof(IApiOperationStore).GetMethod(methodName);
            Assert.NotNull(method);
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.ParameterType == typeof(DateTimeOffset));
        }
    }

    private sealed class BlockingHandler(BlockingHandlerState state) : IApiOperationHandler
    {
        public const string OperationKind = "test.renewal-failure";

        public string Kind => OperationKind;

        public async Task ExecuteAsync(
            Guid operationId,
            string leaseOwner,
            CancellationToken cancellationToken)
        {
            state.Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                    state.Canceled.TrySetResult();
            }
        }
    }

    private sealed class BlockingHandlerState
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TerminalFailureHandler(TerminalFailureState state) : IApiOperationHandler
    {
        public const string OperationKind = "test.terminal-failure";

        public string Kind => OperationKind;

        public Task ExecuteAsync(
            Guid operationId,
            string leaseOwner,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("terminal test failure");

        public Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken)
        {
            Assert.True(
                state.FailurePersisted.Task.IsCompleted,
                "The failed state must be persisted before terminal cleanup starts.");
            state.OperationId = operationId;
            state.TerminalFailureNotified.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class TerminalFailureState
    {
        public Guid OperationId { get; set; }
        public TaskCompletionSource FailurePersisted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TerminalFailureNotified { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
