using System.Security.Cryptography;
using System.Text;
using GZCTF.GuestControl.Contracts;
using GZCTF.GuestSupervisor.Enrollment;
using GZCTF.GuestSupervisor.Lifecycle;
using GZCTF.GuestTelemetry.Contracts;
using GZCTF.GuestTelemetry.Platform;

namespace GZCTF.GuestSupervisor;

public sealed class GuestSupervisorWorker(
    GuestSupervisorConfiguration configuration,
    GuestEnrollmentClient enrollment,
    GuestCheckpointStore checkpointStore,
    GuestIntentStore intentStore,
    GuestLifecycleEngine lifecycle,
    GuestBootstrapPackageExecutor bootstrap,
    GuestRebootController reboot,
    GuestNetworkVerifier network,
    ILogger<GuestSupervisorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GuestLocalCheckpoint? current = null;
        try
        {
        current = await checkpointStore.LoadAsync(
            configuration.Identity, configuration.IntentDigest, stoppingToken);
        if (current.BootChanged)
            await checkpointStore.SaveAsync(current, stoppingToken);
        current = await EmitPendingAsync(current, stoppingToken);
        if (current.Stage == GuestLifecycleStage.Failed)
            throw new InvalidOperationException("guest_terminal_failure_persisted");
        if (current.Stage is null)
        {
            var session = await enrollment.EnsureEnrolledAsync(stoppingToken);
            if (session is not null &&
                !string.Equals(session.Intent.IntentDigest, configuration.IntentDigest, StringComparison.Ordinal))
                throw new InvalidDataException("guest_intent_digest_mismatch");
            if (session is null) throw new InvalidDataException("guest_enrollment_session_missing");
            await intentStore.SaveAsync(session.Intent, stoppingToken);
            current = await AdvanceAndEmitAsync(
                current, null, GuestLifecycleStage.ManagementLinkReady, stoppingToken);
        }
        else
        {
            _ = enrollment.LoadClientCertificate();
        }

        current = await AdvanceIfAsync(current,
            GuestLifecycleStage.ManagementLinkReady, GuestLifecycleStage.GuestEnrolled, stoppingToken);
        if (current.Stage == GuestLifecycleStage.GuestEnrolled)
            await network.VerifyAsync(stoppingToken);
        current = await AdvanceIfAsync(current,
            GuestLifecycleStage.GuestEnrolled, GuestLifecycleStage.NetworkApplied, stoppingToken);
        if (current.Stage == GuestLifecycleStage.RebootRequested)
        {
            if (!current.BootChanged)
                throw new InvalidOperationException("guest_reboot_not_observed");
            current = await AdvanceAndEmitAsync(current,
                GuestLifecycleStage.RebootRequested, GuestLifecycleStage.GuestReenrolledAfterBoot, stoppingToken);
        }
        current = await AdvanceIfAsync(current,
            GuestLifecycleStage.GuestReenrolledAfterBoot, GuestLifecycleStage.BootstrapRunning, stoppingToken);
        current = await AdvanceIfAsync(current,
            GuestLifecycleStage.NetworkApplied, GuestLifecycleStage.BootstrapRunning, stoppingToken);
        if (current.Stage == GuestLifecycleStage.BootstrapRunning)
        {
            var result = await bootstrap.ExecuteAsync(current, stoppingToken);
            if (result.RequiresReboot)
            {
                current = await AdvanceAndEmitAsync(current,
                    GuestLifecycleStage.BootstrapRunning, GuestLifecycleStage.RebootRequested, stoppingToken);
                await reboot.RequestAsync(stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }
            if (!result.Completed) throw new InvalidOperationException("guest_bootstrap_incomplete");
            current = await AdvanceAndEmitAsync(current,
                GuestLifecycleStage.BootstrapRunning, GuestLifecycleStage.BootstrapCompleted, stoppingToken);
        }
        current = await AdvanceIfAsync(current,
            GuestLifecycleStage.BootstrapCompleted, GuestLifecycleStage.ServiceHealthReady, stoppingToken);
        current = await AdvanceIfAsync(current,
            GuestLifecycleStage.ServiceHealthReady, GuestLifecycleStage.ObservationReady, stoppingToken);

        IConnectionProvider provider = OperatingSystem.IsWindows()
            ? new WindowsConnectionProvider()
            : new LinuxConnectionProvider();
        var connections = await provider.ReadAsync(stoppingToken);
        logger.LogInformation(
            "Guest Supervisor ready: Runtime={RuntimeId}, Generation={Generation}, Asset={AssetKey}, Connections={ConnectionCount}",
            current.Identity.RuntimeId, current.Identity.Generation, current.Identity.AssetKey, connections.Count);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var errorCode = ErrorCode(exception);
            var facts = FailureFacts(exception);
            if (current is not null && current.Stage != GuestLifecycleStage.Failed)
            {
                try
                {
                    var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
                        Encoding.UTF8.GetBytes($"{configuration.IntentDigest}:{errorCode}:{current.Sequence + 1}")));
                    current = await lifecycle.AdvanceAsync(
                        current, current.Stage, GuestLifecycleStage.Failed, digest, CancellationToken.None,
                        GuestLifecycleOutcome.Failed, errorCode);
                    await enrollment.PublishEventAsync(new GuestLifecycleEvent(
                        GuestControlProtocol.SchemaVersion,
                        current.Identity,
                        current.Sequence,
                        GuestLifecycleStage.Failed,
                        GuestLifecycleOutcome.Failed,
                        current.UpdatedAt,
                        digest,
                        errorCode,
                        facts), CancellationToken.None);
                    await lifecycle.MarkEmissionAcknowledgedAsync(current, CancellationToken.None);
                }
                catch (Exception projectionError)
                {
                    logger.LogError(projectionError,
                        "Guest failure projection failed: ErrorCode={ErrorCode}", errorCode);
                }
            }
            logger.LogError(exception, "Guest Supervisor stopped at a terminal stage: ErrorCode={ErrorCode}", errorCode);
            throw;
        }
    }

    private static string ErrorCode(Exception exception)
        => exception is GuestBootstrapFailureException bootstrap
            ? bootstrap.ErrorCode
            : ErrorCodeFromMessage(exception);

    private static string ErrorCodeFromMessage(Exception exception)
    {
        var value = exception.Message.Split(':', 2)[0];
        if (value.Length is > 0 and <= 128 &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.'))
            return value;
        return "guest_supervisor_failed";
    }

    private static IReadOnlyDictionary<string, string>? FailureFacts(Exception exception)
    {
        if (exception is not GuestBootstrapFailureException bootstrap)
            return null;

        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["failedStep"] = bootstrap.StepId,
            ["failureCategory"] = bootstrap.Category
        };
        if (bootstrap.ExitCode is { } exitCode)
            facts["exitCode"] = exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return facts;
    }

    private async Task<GuestLocalCheckpoint> AdvanceIfAsync(
        GuestLocalCheckpoint current,
        GuestLifecycleStage expected,
        GuestLifecycleStage next,
        CancellationToken cancellationToken) =>
        current.Stage == expected
            ? await AdvanceAndEmitAsync(current, expected, next, cancellationToken)
            : current;

    private async Task<GuestLocalCheckpoint> AdvanceAndEmitAsync(
        GuestLocalCheckpoint current,
        GuestLifecycleStage? expected,
        GuestLifecycleStage next,
        CancellationToken cancellationToken)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{configuration.IntentDigest}:{next}:{current.Sequence + 1}")));
        current = await lifecycle.AdvanceAsync(
            current, expected, next, $"sha256:{digest}", cancellationToken);
        return await EmitPendingAsync(current, cancellationToken);
    }

    private async Task<GuestLocalCheckpoint> EmitPendingAsync(
        GuestLocalCheckpoint current,
        CancellationToken cancellationToken)
    {
        if (current.Stage is null || current.EmissionAcknowledged) return current;
        await enrollment.PublishEventAsync(new GuestLifecycleEvent(
            GuestControlProtocol.SchemaVersion,
            current.Identity,
            current.Sequence,
            current.Stage.Value,
            current.Outcome,
            current.UpdatedAt,
            current.PayloadDigest!,
            current.ErrorCode), cancellationToken);
        return await lifecycle.MarkEmissionAcknowledgedAsync(current, cancellationToken);
    }
}
