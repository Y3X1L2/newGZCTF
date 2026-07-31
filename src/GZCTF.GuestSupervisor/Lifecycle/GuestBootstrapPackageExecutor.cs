using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.GuestControl.Contracts;
using GZCTF.GuestSupervisor.Enrollment;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed record GuestBootstrapExecutionResult(
    bool Completed,
    bool RequiresReboot,
    int RebootCount,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> PassedHealthChecks);

public sealed class GuestBootstrapPackageExecutor(
    GuestSupervisorConfiguration configuration,
    IGuestGatewayClient enrollment,
    GuestBootstrapExecutionStore executionStore,
    GuestSecretStore secretStore,
    GuestRemoteAccessProvisioner remoteAccess,
    ILogger<GuestBootstrapPackageExecutor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GuestBootstrapExecutionResult> ExecuteAsync(
        GuestLocalCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var intent = await LoadIntentAsync(cancellationToken);
        if (intent.ServicePackage is null)
        {
            await MaterializeRuntimeAsync(intent, cancellationToken);
            var runtimeSecrets = await EnsureSecretsAsync(intent, checkpoint.Identity, cancellationToken);
            await remoteAccess.ApplyAsync(runtimeSecrets, cancellationToken);
            return new GuestBootstrapExecutionResult(true, false, 0, [], []);
        }
        var descriptor = intent.ServicePackage;
        var manifest = GuestPackageContract.VerifyManifest(descriptor);
        var artifactDigest = GuestPackageContract.NormalizeDigest(descriptor.ArtifactDigest);
        var state = await executionStore.LoadAsync(
            intent.IntentDigest, artifactDigest, cancellationToken);
        if (state.RebootCount > manifest.MaxReboots)
            throw new InvalidOperationException("guest_bootstrap_reboot_limit_exceeded");

        var packageRoot = await EnsureArtifactAsync(
            descriptor, checkpoint.Identity, artifactDigest, manifest, cancellationToken);
        await MaterializeRuntimeAsync(intent, cancellationToken);
        var secrets = await EnsureSecretsAsync(intent, checkpoint.Identity, cancellationToken);
        await remoteAccess.ApplyAsync(secrets, cancellationToken);
        var values = new Dictionary<string, string>(intent.Parameters ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var item in secrets) values[item.Key] = item.Value;
        foreach (var parameter in manifest.Parameters)
            if (!values.ContainsKey(parameter.Key) && parameter.DefaultValue is not null)
                values[parameter.Key] = parameter.DefaultValue;
        GuestPackageContract.ValidateValues(manifest, values);
        await MaterializeFilesAsync(manifest, packageRoot, values, cancellationToken);

        var steps = state.Steps.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var step in manifest.Steps)
        {
            if (steps.TryGetValue(step.Id, out var existing))
            {
                if (existing.Status == "Failed")
                    throw new InvalidOperationException($"guest_bootstrap_step_failed:{step.Id}");
                if (existing.Status == "Running")
                    throw new InvalidOperationException($"guest_bootstrap_step_interrupted:{step.Id}");
                if (existing.Status == "Completed") continue;
                if (existing.Status != "RebootPending")
                    throw new InvalidDataException("guest_bootstrap_step_state_invalid");
                if (checkpoint.Identity.BootEpoch != existing.RequestedBootEpoch + 1)
                    throw new InvalidOperationException("guest_bootstrap_reboot_not_observed");
                steps[step.Id] = existing with
                {
                    Status = "Completed",
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                state = state with { Steps = steps };
                await executionStore.SaveAsync(state, cancellationToken);
                continue;
            }

                var entrypoint = GuestPackageContract.SafeCombine(packageRoot, step.Entrypoint);
            if (!File.Exists(entrypoint))
                throw new InvalidDataException($"guest_bootstrap_entrypoint_missing:{step.Id}");
            steps[step.Id] = new GuestStepState(
                "Running", null, null, checkpoint.Identity.BootEpoch, DateTimeOffset.UtcNow);
            state = state with { Steps = steps };
            await executionStore.SaveAsync(state, cancellationToken);
            try
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(entrypoint, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var result = await RunStepAsync(entrypoint, step, cancellationToken);
                var requested = result.ExitCode is 194 or 3010;
                if (!result.Success && !requested)
                {
                    steps[step.Id] = new GuestStepState(
                        "Failed", result.ExitCode, Digest(result.Output), checkpoint.Identity.BootEpoch, DateTimeOffset.UtcNow);
                    state = state with { Steps = steps };
                    await executionStore.SaveAsync(state, cancellationToken);
                    throw new InvalidOperationException($"guest_bootstrap_step_failed:{step.Id}");
                }
                var reboot = string.Equals(step.Reboot, "Required", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(step.Reboot, "IfRequested", StringComparison.OrdinalIgnoreCase) && requested;
                if (reboot && state.RebootCount >= manifest.MaxReboots)
                {
                    steps[step.Id] = new GuestStepState(
                        "Failed", result.ExitCode, Digest(result.Output), checkpoint.Identity.BootEpoch, DateTimeOffset.UtcNow);
                    state = state with { Steps = steps };
                    await executionStore.SaveAsync(state, cancellationToken);
                    throw new InvalidOperationException("guest_bootstrap_reboot_limit_exceeded");
                }
                steps[step.Id] = new GuestStepState(
                    reboot ? "RebootPending" : "Completed",
                    result.ExitCode,
                    Digest(result.Output),
                    checkpoint.Identity.BootEpoch,
                    DateTimeOffset.UtcNow);
                state = state with
                {
                    RebootCount = state.RebootCount + (reboot ? 1 : 0),
                    Steps = steps
                };
                await executionStore.SaveAsync(state, cancellationToken);
                if (reboot)
                    return new GuestBootstrapExecutionResult(false, true, state.RebootCount,
                        CompletedSteps(steps), state.PassedHealthChecks);
            }
            catch (OperationCanceledException) { throw; }
            catch when (steps[step.Id].Status == "Failed") { throw; }
            catch (Exception exception)
            {
                steps[step.Id] = new GuestStepState(
                    "Failed", null, Digest(exception.Message), checkpoint.Identity.BootEpoch, DateTimeOffset.UtcNow);
                state = state with { Steps = steps };
                await executionStore.SaveAsync(state, cancellationToken);
                throw;
            }
        }

        var checks = await RunHealthChecksAsync(
            manifest, packageRoot, state.PassedHealthChecks, values, cancellationToken);
        state = state with { Steps = steps, PassedHealthChecks = checks };
        await executionStore.SaveAsync(state, cancellationToken);
        logger.LogInformation(
            "Guest bootstrap completed: Runtime={RuntimeId}, Generation={Generation}, Asset={AssetKey}, Steps={StepCount}",
            checkpoint.Identity.RuntimeId, checkpoint.Identity.Generation, checkpoint.Identity.AssetKey, steps.Count);
        return new GuestBootstrapExecutionResult(true, false, state.RebootCount,
            CompletedSteps(steps), checks);
    }

    private async Task<GuestBootstrapIntent> LoadIntentAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(configuration.StateRoot, "intent.json");
        if (!File.Exists(path)) throw new InvalidDataException("guest_intent_missing");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GuestBootstrapIntent>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("guest_intent_invalid");
    }

    private async Task<string> EnsureArtifactAsync(
        GuestServicePackageDescriptor descriptor,
        GuestAssetIdentity identity,
        string artifactDigest,
        GuestPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var archive = await enrollment.DownloadArtifactAsync(
            descriptor, identity, cancellationToken);
        var root = Path.Combine(configuration.StateRoot, "packages", artifactDigest);
        var marker = Path.Combine(root, ".verified");
        if (!File.Exists(marker))
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            await GuestPackageContract.ExtractArchiveAsync(archive, root, cancellationToken);
            await File.WriteAllTextAsync(marker, artifactDigest, cancellationToken);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(marker, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        foreach (var file in manifest.Files)
            if (!File.Exists(GuestPackageContract.SafeCombine(root, file.SourcePath)))
                throw new InvalidDataException($"guest_bootstrap_file_missing:{file.SourcePath}");
        foreach (var step in manifest.Steps)
            if (!File.Exists(GuestPackageContract.SafeCombine(root, step.Entrypoint)))
                throw new InvalidDataException($"guest_bootstrap_entrypoint_missing:{step.Id}");
        return root;
    }

    private async Task<IReadOnlyDictionary<string, string>> EnsureSecretsAsync(
        GuestBootstrapIntent intent,
        GuestAssetIdentity identity,
        CancellationToken cancellationToken)
    {
        var references = intent.SecretReferences ?? [];
        if (references.Count == 0) return new Dictionary<string, string>();
        var response = await enrollment.FetchSecretsAsync(
            identity, references.Select(item => item.Reference).ToArray(), cancellationToken);
        if (response.Secrets.Count != references.Count ||
            response.Secrets.Any(item => !references.Any(reference => reference.Reference == item.Reference)))
            throw new InvalidDataException("guest_secret_response_mismatch");
        await secretStore.MaterializeAsync(response.Secrets, cancellationToken);
        return response.Secrets.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
    }

    private static async Task MaterializeFilesAsync(
        GuestPackageManifest manifest,
        string packageRoot,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        foreach (var file in manifest.Files)
        {
            var bytes = await File.ReadAllBytesAsync(GuestPackageContract.SafeCombine(packageRoot, file.SourcePath), cancellationToken);
            if (file.Template)
            {
                var text = Encoding.UTF8.GetString(bytes);
                foreach (var value in values)
                    text = text.Replace($"${{{value.Key}}}", value.Value, StringComparison.Ordinal);
                bytes = Encoding.UTF8.GetBytes(text);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);
            var temporary = file.TargetPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                File.Move(temporary, file.TargetPath, true);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(file.TargetPath, GuestPackageContract.ParseUnixMode(file.Mode));
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static async Task MaterializeRuntimeAsync(
        GuestBootstrapIntent intent,
        CancellationToken cancellationToken)
    {
        if (intent.Parameters is null) return;
        var root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GZCTF", "Runtime")
            : "/opt/gzctf/runtime";
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "runtime.json");
        await File.WriteAllBytesAsync(path,
            JsonSerializer.SerializeToUtf8Bytes(new { Parameters = intent.Parameters }, JsonOptions), cancellationToken);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private async Task<ProcessResult> RunStepAsync(
        string entrypoint,
        GuestPackageStep step,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            start.FileName = "powershell.exe";
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(entrypoint);
        }
        else
        {
            start.FileName = entrypoint;
        }
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("guest_bootstrap_process_start_failed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(step.TimeoutSeconds, 1, 3600)));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(false, null, "timeout", "");
        }
        var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken) +
                      await process.StandardError.ReadToEndAsync(cancellationToken));
        return new ProcessResult(process.ExitCode == 0, process.ExitCode, "completed", output);
    }

    private async Task<IReadOnlyList<string>> RunHealthChecksAsync(
        GuestPackageManifest manifest,
        string packageRoot,
        IReadOnlyList<string> previous,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var passed = previous.ToHashSet(StringComparer.Ordinal);
        foreach (var check in manifest.HealthChecks)
        {
            if (passed.Contains(check.Id)) continue;
            var success = false;
            for (var attempt = 0; attempt < Math.Clamp(check.Attempts, 1, 120); attempt++)
            {
                var target = check.Target;
                foreach (var value in values)
                    target = target.Replace($"${{{value.Key}}}", value.Value, StringComparison.Ordinal);
                if (string.Equals(check.Kind, "Entrypoint", StringComparison.OrdinalIgnoreCase))
                    target = GuestPackageContract.SafeCombine(packageRoot, target);
                success = await CheckAsync(check with { Target = target }, cancellationToken);
                if (success) break;
                if (attempt + 1 < check.Attempts)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(check.TimeoutSeconds, 1, 60)), cancellationToken);
            }
            if (!success) throw new InvalidOperationException($"guest_health_check_failed:{check.Id}");
            passed.Add(check.Id);
        }
        return passed.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<bool> CheckAsync(GuestPackageHealthCheck check, CancellationToken cancellationToken)
    {
        if (string.Equals(check.Kind, "Tcp", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(check.Target, out var port) && port is > 0 and <= 65535)
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(check.TimeoutSeconds, 1, 60)));
            try { await client.ConnectAsync("127.0.0.1", port, timeout.Token); return true; }
            catch (SocketException) { return false; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        }
        if (string.Equals(check.Kind, "Http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(check.Target, UriKind.Absolute, out var uri))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(check.TimeoutSeconds, 1, 60)) };
            try { using var response = await client.GetAsync(uri, cancellationToken); return (int)response.StatusCode < 500; }
            catch (HttpRequestException) { return false; }
        }
        if (string.Equals(check.Kind, "Entrypoint", StringComparison.OrdinalIgnoreCase) && File.Exists(check.Target))
        {
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : check.Target,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (OperatingSystem.IsWindows())
            {
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-NonInteractive");
                start.ArgumentList.Add("-File");
                start.ArgumentList.Add(check.Target);
            }
            else
                File.SetUnixFileMode(check.Target,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var process = Process.Start(start);
            if (process is null) return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(check.TimeoutSeconds, 1, 60)));
            try { await process.WaitForExitAsync(timeout.Token); return process.ExitCode == 0; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }
        }
        throw new InvalidDataException($"guest_health_check_invalid:{check.Id}");
    }

    private static IReadOnlyList<string> CompletedSteps(IReadOnlyDictionary<string, GuestStepState> steps) =>
        steps.Where(item => item.Value.Status == "Completed").Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();

    private static string Digest(string value) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")));

    private sealed record ProcessResult(bool Success, int? ExitCode, string Category, string Output);
}
