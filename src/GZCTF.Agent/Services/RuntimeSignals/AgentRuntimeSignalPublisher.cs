using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.RuntimeSignals;

public sealed class AgentRuntimeSignalPublisher(
    AgentRuntimeSignalJournal journal,
    IHttpClientFactory clientFactory,
    IOptions<AgentConfig> options,
    ILogger<AgentRuntimeSignalPublisher> logger)
{
    private readonly AgentConfig _config = options.Value;
    private readonly Channel<Guid> _pending = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1024)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public async Task<AgentRuntimeSignalModel> AppendAsync(
        AgentRuntimeSignalDraft draft,
        CancellationToken cancellationToken)
    {
        var signal = await journal.AppendAsync(draft, cancellationToken);
        _pending.Writer.TryWrite(signal.OperationId);
        return signal;
    }

    internal async Task PublishPendingAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var signals = await journal.ReadPendingAsync(operationId, cancellationToken);
        foreach (var batch in signals.Chunk(256))
        {
            var client = clientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.AuthToken);
            using var response = await client.PostAsJsonAsync(
                $"{_config.ServerUrl.TrimEnd('/')}/api/internal/teamlab/runtime-signals/batch",
                new AgentRuntimeSignalBatchModel(_config.NodeId, batch),
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                await journal.AcknowledgeAsync(operationId, batch[^1].Sequence, cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Runtime signal batch delivery failed: operation={OperationId}, count={Count}, status={Status}",
                    operationId, batch.Length, (int)response.StatusCode);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<AgentRuntimeSignalIngestResult[]>(
                cancellationToken);
            if (result is null || result.Length != batch.Length ||
                result.Any(item => !item.Accepted && !item.Duplicate && !item.Stale))
                return;
            await journal.AcknowledgeAsync(operationId, batch[^1].Sequence, cancellationToken);
        }
    }

    internal IAsyncEnumerable<Guid> ReadPendingNotificationsAsync(CancellationToken cancellationToken) =>
        _pending.Reader.ReadAllAsync(cancellationToken);

    internal void Schedule(Guid operationId) => _pending.Writer.TryWrite(operationId);

    internal bool TryReadPendingNotification(out Guid operationId) =>
        _pending.Reader.TryRead(out operationId);
}

public sealed class AgentRuntimeSignalPublisherWorker(
    AgentRuntimeSignalJournal journal,
    AgentRuntimeSignalPublisher publisher,
    ILogger<AgentRuntimeSignalPublisherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var operationId in journal.ListOperations()) publisher.Schedule(operationId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = new HashSet<Guid>();
                await foreach (var operationId in publisher.ReadPendingNotificationsAsync(stoppingToken))
                {
                    pending.Add(operationId);
                    while (publisher.TryReadPendingNotification(out var queued)) pending.Add(queued);
                    break;
                }
                await Parallel.ForEachAsync(
                    pending.Order(),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 16,
                        CancellationToken = stoppingToken
                    },
                    (operationId, token) =>
                        new ValueTask(publisher.PublishPendingAsync(operationId, token)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Runtime signal replay failed; pending journals will be retried");
            }

            foreach (var operationId in journal.ListOperations()) publisher.Schedule(operationId);
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
