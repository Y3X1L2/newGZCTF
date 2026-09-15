using System.Text.Json;
using System.Security.Cryptography;
using GZCTF.Models;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.Runtime.Application;

public sealed record RuntimeSignalWaitResult(
    bool Ready,
    bool Failed,
    string? ErrorCode,
    long Sequence,
    AgentRuntimeSignalStage? Stage,
    IReadOnlyDictionary<string, string>? Facts = null);

public sealed class RuntimeSignalConflictException(string message) : Exception(message);
public sealed class RuntimeSignalNodeNotFoundException : Exception { }
public sealed class RuntimeSignalAuthenticationException : Exception { }

public sealed class RuntimeSignalService(
    AppDbContext context,
    IRuntimeSignalWakeup wakeup,
    IServiceScopeFactory scopeFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AgentRuntimeSignalIngestResult> IngestAuthenticatedAsync(
        Guid workerNodeId,
        string authToken,
        AgentRuntimeSignalModel model,
        CancellationToken cancellationToken)
    {
        var expectedToken = await context.WorkerNodes.AsNoTracking()
            .Where(item => item.Id == workerNodeId)
            .Select(item => item.AuthToken)
            .SingleOrDefaultAsync(cancellationToken);
        if (expectedToken is null) throw new RuntimeSignalNodeNotFoundException();
        if (!FixedTimeEquals(authToken, expectedToken)) throw new RuntimeSignalAuthenticationException();
        return (await IngestBatchAsync(workerNodeId, [model], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<AgentRuntimeSignalIngestResult>> IngestBatchAuthenticatedAsync(
        Guid workerNodeId,
        string authToken,
        IReadOnlyList<AgentRuntimeSignalModel> models,
        CancellationToken cancellationToken)
    {
        if (models.Count is < 1 or > 256)
            throw new ArgumentException("The runtime signal batch must contain between 1 and 256 signals.", nameof(models));
        var expectedToken = await context.WorkerNodes.AsNoTracking()
            .Where(item => item.Id == workerNodeId)
            .Select(item => item.AuthToken)
            .SingleOrDefaultAsync(cancellationToken);
        if (expectedToken is null) throw new RuntimeSignalNodeNotFoundException();
        if (!FixedTimeEquals(authToken, expectedToken)) throw new RuntimeSignalAuthenticationException();
        return await IngestBatchAsync(workerNodeId, models, cancellationToken);
    }

    public async Task<AgentRuntimeSignalIngestResult> IngestAsync(
        Guid workerNodeId,
        AgentRuntimeSignalModel model,
        CancellationToken cancellationToken)
    {
        return (await IngestBatchAsync(workerNodeId, [model], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<AgentRuntimeSignalIngestResult>> IngestBatchAsync(
        Guid workerNodeId,
        IReadOnlyList<AgentRuntimeSignalModel> models,
        CancellationToken cancellationToken)
    {
        if (models.Count is < 1 or > 256)
            throw new ArgumentException("The runtime signal batch must contain between 1 and 256 signals.", nameof(models));
        var prepared = models.Select(model =>
        {
            Validate(model);
            return new PreparedSignal(model, Convert.ToHexStringLower(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(model, JsonOptions))));
        }).ToArray();
        foreach (var repeated in prepared.GroupBy(item => (item.Model.OperationId, item.Model.Sequence)))
            if (repeated.Select(item => item.PayloadHash).Distinct(StringComparer.Ordinal).Skip(1).Any())
                throw new RuntimeSignalConflictException(
                    "The runtime signal sequence was reused with a different payload.");

        var runtimeIds = prepared.Select(item => item.Model.RuntimeId).Distinct().ToArray();
        var runtimes = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => runtimeIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Generation })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var operationIds = prepared.Select(item => item.Model.OperationId).Distinct().ToArray();
        var assets = await context.TeamLabRuntimeAssets
            .Where(item => item.WorkerNodeId == workerNodeId && item.AgentOperationId.HasValue &&
                           operationIds.Contains(item.AgentOperationId.Value))
            .ToDictionaryAsync(item => item.AgentOperationId!.Value, cancellationToken);
        var existing = await context.AgentRuntimeSignals.AsNoTracking()
            .Where(item => item.WorkerNodeId == workerNodeId && operationIds.Contains(item.OperationId))
            .Select(item => new { item.OperationId, item.Sequence, item.PayloadHash })
            .ToArrayAsync(cancellationToken);
        var latestByOperation = existing.GroupBy(item => item.OperationId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Sequence));
        var existingBySequence = existing.ToDictionary(item => (item.OperationId, item.Sequence));
        var results = new List<AgentRuntimeSignalIngestResult>(prepared.Length);
        var acceptedOperations = new HashSet<Guid>();

        foreach (var item in prepared)
        {
            var model = item.Model;
            if (!runtimes.TryGetValue(model.RuntimeId, out var runtime))
                throw new InvalidOperationException("The TeamLab runtime does not exist.");
            if (runtime.Generation != model.Generation)
            {
                results.Add(new AgentRuntimeSignalIngestResult(false, false, true, model.Sequence));
                continue;
            }
            if (!assets.TryGetValue(model.OperationId, out var asset) || asset.RuntimeId != model.RuntimeId ||
                asset.Generation != model.Generation)
                throw new InvalidOperationException("The runtime signal operation is not owned by this node.");
            if (existingBySequence.TryGetValue((model.OperationId, model.Sequence), out var duplicate))
            {
                if (!string.Equals(duplicate.PayloadHash, item.PayloadHash, StringComparison.Ordinal))
                    throw new RuntimeSignalConflictException(
                        "The runtime signal sequence was reused with a different payload.");
                results.Add(new AgentRuntimeSignalIngestResult(false, true, false, model.Sequence));
                continue;
            }
            if (latestByOperation.GetValueOrDefault(model.OperationId) > model.Sequence)
            {
                results.Add(new AgentRuntimeSignalIngestResult(false, false, true, model.Sequence));
                continue;
            }

            context.AgentRuntimeSignals.Add(new AgentRuntimeSignal
            {
                OperationId = model.OperationId,
                WorkerNodeId = workerNodeId,
                RuntimeId = model.RuntimeId,
                Generation = model.Generation,
                ResourceKind = model.ResourceKind.Trim(),
                ResourceId = model.ResourceId.Trim(),
                Sequence = model.Sequence,
                Stage = model.Stage,
                Outcome = model.Outcome,
                ObservedAt = model.ObservedAt,
                ErrorCode = NullIfWhiteSpace(model.ErrorCode),
                PayloadHash = item.PayloadHash,
                Retryable = model.Retryable,
                FactsJson = JsonSerializer.Serialize(model.Facts ?? new Dictionary<string, string>(), JsonOptions)
            });
            latestByOperation[model.OperationId] = model.Sequence;
            asset.AgentSignalSequence = Math.Max(asset.AgentSignalSequence, model.Sequence);
            acceptedOperations.Add(model.OperationId);
            results.Add(new AgentRuntimeSignalIngestResult(true, false, false, model.Sequence));
        }

        if (acceptedOperations.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
        foreach (var operationId in acceptedOperations)
            await wakeup.NotifyAsync(operationId, cancellationToken);
        return results;
    }

    private sealed record PreparedSignal(AgentRuntimeSignalModel Model, string PayloadHash);

    public async Task<RuntimeSignalWaitResult> WaitForAsync(
        Guid operationId,
        int generation,
        AgentRuntimeSignalStage expectedStage,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => await WaitForCoreAsync(
            operationId, generation, expectedStage, timeout, cancellationToken);

    public async Task<RuntimeSignalWaitResult> WaitForAsync(
        Guid operationId,
        int generation,
        AgentRuntimeSignalStage expectedStage,
        CancellationToken cancellationToken)
        => await WaitForCoreAsync(
            operationId, generation, expectedStage, null, cancellationToken);

    private async Task<RuntimeSignalWaitResult> WaitForCoreAsync(
        Guid operationId,
        int generation,
        AgentRuntimeSignalStage expectedStage,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } boundedTimeout)
            deadline.CancelAfter(boundedTimeout);
        try
        {
            while (!deadline.IsCancellationRequested)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var readContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var signal = await readContext.AgentRuntimeSignals.AsNoTracking()
                    .Where(item => item.OperationId == operationId && item.Generation == generation)
                    .OrderByDescending(item => item.Sequence)
                    .FirstOrDefaultAsync(deadline.Token);
                if (signal is not null)
                {
                    if (signal.Outcome == AgentRuntimeSignalOutcome.Failed ||
                        signal.Stage == AgentRuntimeSignalStage.Failed)
                        return new RuntimeSignalWaitResult(
                            false, true, signal.ErrorCode, signal.Sequence, signal.Stage,
                            ParseFacts(signal.FactsJson));
                    if (signal.Outcome == AgentRuntimeSignalOutcome.Ready && Reached(signal.Stage, expectedStage))
                        return new RuntimeSignalWaitResult(
                            true, false, null, signal.Sequence, signal.Stage);
                }

                await wakeup.WaitAsync(operationId, TimeSpan.FromSeconds(1), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        return timeout is null
            ? throw new OperationCanceledException(cancellationToken)
            : new RuntimeSignalWaitResult(false, true, "runtime.signal_timeout", 0, null);
    }

    private static void Validate(AgentRuntimeSignalModel model)
    {
        if (model.OperationId == Guid.Empty || model.RuntimeId <= 0 || model.Generation <= 0 ||
            model.Sequence <= 0 || string.IsNullOrWhiteSpace(model.ResourceKind) ||
            model.ResourceKind.Length > 64 || string.IsNullOrWhiteSpace(model.ResourceId) ||
            model.ResourceId.Length > 256 || model.ObservedAt == default)
            throw new ArgumentException("The runtime signal is invalid.", nameof(model));
        if (model.ErrorCode?.Length > 128 || model.Facts is { Count: > 32 })
            throw new ArgumentException("The runtime signal exceeds its bounds.", nameof(model));
        if (model.Facts is null) return;
        foreach (var (key, value) in model.Facts)
            if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || value.Length > 256)
                throw new ArgumentException("The runtime signal facts exceed their bounds.", nameof(model));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string>? ParseFacts(string factsJson)
    {
        try
        {
            var facts = JsonSerializer.Deserialize<Dictionary<string, string>>(factsJson, JsonOptions);
            return facts is { Count: > 0 } ? facts : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    internal static bool Reached(AgentRuntimeSignalStage actual, AgentRuntimeSignalStage expected)
    {
        if (actual == expected) return true;
        var actualRank = StageRank(actual);
        var expectedRank = StageRank(expected);
        return actualRank.Group == expectedRank.Group && actualRank.Rank >= expectedRank.Rank;
    }

    private static (byte Group, byte Rank) StageRank(AgentRuntimeSignalStage stage) => stage switch
    {
        AgentRuntimeSignalStage.ResourceCreated => (0, 0),
        AgentRuntimeSignalStage.NetworkReady => (0, 1),
        AgentRuntimeSignalStage.DomainRunning => (0, 2),
        AgentRuntimeSignalStage.GuestReady => (0, 3),
        AgentRuntimeSignalStage.ManagementLinkReady => (1, 0),
        AgentRuntimeSignalStage.GuestEnrolled => (1, 1),
        AgentRuntimeSignalStage.NetworkApplied => (1, 2),
        AgentRuntimeSignalStage.BootstrapRunning => (1, 3),
        AgentRuntimeSignalStage.Rebooting => (1, 4),
        AgentRuntimeSignalStage.GuestReadyAfterReboot => (0, 6),
        AgentRuntimeSignalStage.GuestReenrolledAfterBoot => (1, 5),
        AgentRuntimeSignalStage.BootstrapCompleted => (1, 6),
        AgentRuntimeSignalStage.HealthReady => (1, 7),
        AgentRuntimeSignalStage.ObservationReady => (1, 8),
        AgentRuntimeSignalStage.Failed => (byte.MaxValue, byte.MaxValue),
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };
}
