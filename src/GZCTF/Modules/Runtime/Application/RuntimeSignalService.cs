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
        return await IngestAsync(workerNodeId, model, cancellationToken);
    }

    public async Task<AgentRuntimeSignalIngestResult> IngestAsync(
        Guid workerNodeId,
        AgentRuntimeSignalModel model,
        CancellationToken cancellationToken)
    {
        Validate(model);
        var payloadHash = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(model, JsonOptions)));
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == model.RuntimeId, cancellationToken)
            ?? throw new InvalidOperationException("The TeamLab runtime does not exist.");
        if (runtime.Generation != model.Generation)
            return new AgentRuntimeSignalIngestResult(false, false, true, model.Sequence);
        var ownedAsset = await context.TeamLabRuntimeAssets.SingleOrDefaultAsync(item =>
            item.RuntimeId == model.RuntimeId && item.Generation == model.Generation &&
            item.WorkerNodeId == workerNodeId && item.AgentOperationId == model.OperationId,
            cancellationToken);
        if (ownedAsset is null)
            throw new InvalidOperationException("The runtime signal operation is not owned by this node.");

        var latest = await context.AgentRuntimeSignals.AsNoTracking()
            .Where(item => item.WorkerNodeId == workerNodeId && item.OperationId == model.OperationId)
            .OrderByDescending(item => item.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null && model.Sequence < latest.Sequence)
            return new AgentRuntimeSignalIngestResult(false, false, true, model.Sequence);
        if (latest is not null && model.Sequence == latest.Sequence)
        {
            if (!string.Equals(latest.PayloadHash, payloadHash, StringComparison.Ordinal))
                throw new RuntimeSignalConflictException(
                    "The runtime signal sequence was reused with a different payload.");
            return new AgentRuntimeSignalIngestResult(false, true, false, model.Sequence);
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
            PayloadHash = payloadHash,
            Retryable = model.Retryable,
            FactsJson = JsonSerializer.Serialize(model.Facts ?? new Dictionary<string, string>(), JsonOptions)
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            return new AgentRuntimeSignalIngestResult(false, true, false, model.Sequence);
        }

        await context.TeamLabRuntimeAssets
            .Where(item => item.Id == ownedAsset.Id && item.AgentSignalSequence < model.Sequence)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.AgentSignalSequence, model.Sequence), cancellationToken);

        await wakeup.NotifyAsync(model.OperationId, cancellationToken);
        return new AgentRuntimeSignalIngestResult(true, false, false, model.Sequence);
    }

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
