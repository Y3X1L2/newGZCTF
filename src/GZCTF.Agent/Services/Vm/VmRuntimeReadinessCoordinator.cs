using System.Collections.Concurrent;
using System.Threading.Channels;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.RuntimeSignals;

namespace GZCTF.Agent.Services.Vm;

public sealed class VmRuntimeReadinessCoordinator(
    KvmService kvm,
    VmGuestAgentService guest,
    AgentRuntimeSignalJournal journal,
    AgentRuntimeSignalPublisher signals,
    ILogger<VmRuntimeReadinessCoordinator> logger) : BackgroundService
{
    private const int WorkerCount = 8;
    private static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private readonly Channel<Guid> _pending = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<Guid, byte> _scheduled = new();
    private readonly ConcurrentDictionary<Guid, byte> _warningLogged = new();

    public async Task TrackAsync(
        CreateVmRequest request,
        CreateVmResponse response,
        CancellationToken cancellationToken)
    {
        if (request.OperationId is not { } operationId || operationId == Guid.Empty)
            return;
        if (request.RuntimeId <= 0 || request.Generation <= 0 || string.IsNullOrWhiteSpace(response.NativeId))
            throw new InvalidOperationException("A TeamLab VM readiness operation has invalid identity facts.");

        var existing = await journal.ReadAllAsync(operationId, cancellationToken);
        var latest = existing.LastOrDefault();
        if (latest is not null &&
            (latest.RuntimeId != request.RuntimeId || latest.Generation != request.Generation ||
             !string.Equals(latest.ResourceId, response.VmName, StringComparison.Ordinal)))
            throw new InvalidOperationException("The VM readiness operation identity conflicts with its journal.");
        if (latest is null)
        {
            await signals.AppendAsync(new AgentRuntimeSignalDraft(
                operationId,
                request.RuntimeId,
                request.Generation,
                "vm",
                response.VmName,
                AgentRuntimeSignalStage.DomainRunning,
                AgentRuntimeSignalOutcome.Ready,
                Facts: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nativeId"] = response.NativeId,
                    ["osType"] = (request.GuestControl.OsType ?? VmInitOsType.Linux).ToString(),
                    ["warningAfterSeconds"] = Math.Clamp(request.GuestReadyWarningAfterSeconds, 30, 3600).ToString()
                }), cancellationToken);
        }
        Schedule(operationId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var operationId in journal.ListOperations()) Schedule(operationId);
        await Task.WhenAll(Enumerable.Range(0, WorkerCount).Select(_ => ProcessAsync(stoppingToken)));
    }

    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? operationId = null;
            try
            {
                operationId = await _pending.Reader.ReadAsync(stoppingToken);
                var completed = await AdvanceAsync(operationId.Value, stoppingToken);
                if (completed)
                {
                    _scheduled.TryRemove(operationId.Value, out _);
                    _warningLogged.TryRemove(operationId.Value, out _);
                    continue;
                }
                await Task.Delay(RetryDelay, stoppingToken);
                if (!_pending.Writer.TryWrite(operationId.Value))
                    _scheduled.TryRemove(operationId.Value, out _);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "VM readiness coordination failed; the persisted operation will be retried");
                if (operationId is { } retryOperation)
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                    if (!_pending.Writer.TryWrite(retryOperation))
                        _scheduled.TryRemove(retryOperation, out _);
                }
            }
        }
    }

    private async Task<bool> AdvanceAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var history = await journal.ReadAllAsync(operationId, cancellationToken);
        var latest = history.LastOrDefault();
        if (latest is null || latest.ResourceKind != "vm" ||
            latest.Outcome == AgentRuntimeSignalOutcome.Failed ||
            latest.Stage >= AgentRuntimeSignalStage.GuestReady)
            return true;
        if (latest.Stage != AgentRuntimeSignalStage.DomainRunning || latest.Facts is null ||
            !latest.Facts.TryGetValue("nativeId", out var nativeId) ||
            !TryGetWarningAfterSeconds(latest.Facts, out var warningAfterSeconds))
            throw new InvalidDataException("The VM readiness journal does not contain resumable domain facts.");

        VmGuestStatusResponse status;
        try
        {
            status = await kvm.ExecuteWithIdentityAsync(
                latest.ResourceId,
                latest.Generation,
                nativeId,
                token => guest.WaitReadyAsync(
                    latest.ResourceId,
                    ProbeWindow,
                    token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AgentOperationException exception) when (!exception.Retryable)
        {
            await AppendFailureAsync(latest, "runtime.guest_ready_probe_failed", exception.Message, cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "VM guest readiness probe will be retried: runtime={RuntimeId}, generation={Generation}, vm={VmName}",
                latest.RuntimeId, latest.Generation, latest.ResourceId);
            return false;
        }

        if (!status.Ready)
        {
            if (DateTimeOffset.UtcNow - latest.ObservedAt >=
                TimeSpan.FromSeconds(Math.Clamp(warningAfterSeconds, 30, 3600)) &&
                _warningLogged.TryAdd(operationId, 0))
                logger.LogWarning(
                    "VM guest readiness is slower than the observation threshold and remains pending: runtime={RuntimeId}, generation={Generation}, vm={VmName}, thresholdSeconds={ThresholdSeconds}, detail={Detail}",
                    latest.RuntimeId, latest.Generation, latest.ResourceId, warningAfterSeconds, status.Message);
            return false;
        }
        await signals.AppendAsync(new AgentRuntimeSignalDraft(
            latest.OperationId,
            latest.RuntimeId,
            latest.Generation,
            "vm",
            latest.ResourceId,
            AgentRuntimeSignalStage.GuestReady,
            AgentRuntimeSignalOutcome.Ready,
            Facts: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nativeId"] = nativeId,
                ["qgaVersion"] = status.Version ?? "unknown"
            }), cancellationToken);
        return true;
    }

    private async Task AppendFailureAsync(
        AgentRuntimeSignalModel latest,
        string errorCode,
        string detail,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "VM guest readiness failed: runtime={RuntimeId}, generation={Generation}, vm={VmName}, code={ErrorCode}, detail={Detail}",
            latest.RuntimeId, latest.Generation, latest.ResourceId, errorCode, detail);
        await signals.AppendAsync(new AgentRuntimeSignalDraft(
            latest.OperationId,
            latest.RuntimeId,
            latest.Generation,
            "vm",
            latest.ResourceId,
            AgentRuntimeSignalStage.Failed,
            AgentRuntimeSignalOutcome.Failed,
            errorCode,
            Retryable: false), cancellationToken);
    }

    private void Schedule(Guid operationId)
    {
        if (_scheduled.TryAdd(operationId, 0) && !_pending.Writer.TryWrite(operationId))
            _scheduled.TryRemove(operationId, out _);
    }

    private static bool TryGetWarningAfterSeconds(
        IReadOnlyDictionary<string, string> facts,
        out int warningAfterSeconds)
    {
        warningAfterSeconds = 0;
        if (facts.TryGetValue("warningAfterSeconds", out var warningValue) &&
            int.TryParse(warningValue, out warningAfterSeconds))
            return true;
        return facts.TryGetValue("deadlineSeconds", out var legacyValue) &&
               int.TryParse(legacyValue, out warningAfterSeconds);
    }
}
