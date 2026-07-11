using System.Collections.Concurrent;
using GZCTF.Modules.Audit.Application;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Integration.Test.Tests.Api.Fixtures;

public sealed class CompletingApiOperationHandler(
    AppDbContext context,
    ApiOperationHandlerExecutionRecorder recorder) : IApiOperationHandler
{
    private readonly Guid _instanceId = Guid.NewGuid();

    public string Kind => "test.complete";

    public async Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        _ = await context.ApiOperations.AsNoTracking()
            .SingleAsync(operation => operation.Id == operationId, cancellationToken);
        recorder.Record(operationId, _instanceId);
    }
}

public sealed record ApiOperationHandlerExecution(Guid OperationId, Guid HandlerInstanceId);

public sealed class ApiOperationHandlerExecutionRecorder
{
    private readonly ConcurrentQueue<ApiOperationHandlerExecution> _executions = new();

    public void Record(Guid operationId, Guid handlerInstanceId) =>
        _executions.Enqueue(new ApiOperationHandlerExecution(operationId, handlerInstanceId));

    public IReadOnlyList<ApiOperationHandlerExecution> Snapshot() => _executions.ToArray();
}
