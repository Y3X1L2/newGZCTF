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
        foreach (var signal in await journal.ReadPendingAsync(operationId, cancellationToken))
        {
            var client = clientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.AuthToken);
            using var response = await client.PostAsJsonAsync(
                $"{_config.ServerUrl.TrimEnd('/')}/api/v1/nodes/{_config.NodeId:D}/runtime-signals",
                signal,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    var reason = await TryReadRejectionAsync(response, cancellationToken);
                    logger.LogWarning(
                        "Runtime signal conflict is terminal and was discarded: operation={OperationId}, sequence={Sequence}, reason={Reason}",
                        signal.OperationId, signal.Sequence, reason);
                    await journal.AcknowledgeAsync(operationId, signal.Sequence, cancellationToken);
                    continue;
                }
                logger.LogWarning(
                    "Runtime signal delivery failed: operation={OperationId}, sequence={Sequence}, status={Status}",
                    signal.OperationId, signal.Sequence, (int)response.StatusCode);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<AgentRuntimeSignalIngestResult>(
                cancellationToken);
            if (result is null || !result.Accepted && !result.Duplicate && !result.Stale)
                return;
            await journal.AcknowledgeAsync(operationId, signal.Sequence, cancellationToken);
        }
    }

    private static async Task<string> TryReadRejectionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        }
        catch
        {
            return string.Empty;
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
