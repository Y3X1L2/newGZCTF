using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Vm;

public sealed partial class VmScenarioArtifactService(
    VmGuestAgentService guestAgent,
    AgentOciArtifactUploader uploader,
    AgentOperationGate gate,
    AgentOperationReceiptStore receipts,
    IOptions<KvmConfig> options)
{
    private readonly KvmConfig _config = options.Value;

    public async Task<CommitVmScenarioResponse> CommitAsync(
        CommitVmScenarioRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        return await receipts.ExecuteAsync(
            "scenario-commit",
            request.OperationId,
            request,
            token => CommitCoreAsync(request, token),
            cancellationToken);
    }

    private async Task<CommitVmScenarioResponse> CommitCoreAsync(
        CommitVmScenarioRequest request,
        CancellationToken cancellationToken)
    {
        var registryTarget = new AgentOciRegistryTarget(
            request.RegistryTarget.RegistryAddress,
            request.RegistryTarget.Repository,
            request.RegistryTarget.Tag);
        var annotations = BuildAnnotations(request);
        var checkpointRoot = Path.Combine(Path.GetFullPath(_config.ImageStoragePath), "scenario-commit-checkpoints");
        var sanitized = Path.Combine(checkpointRoot, request.OperationId.ToString("N") + ".marker");
        var recovered = await uploader.TryResolveAsync(registryTarget, annotations, cancellationToken);
        if (recovered is not null)
        {
            TryDeleteFile(sanitized);
            return BuildResponse(request, recovered);
        }

        await using var permit = await gate.EnterAsync(AgentOperationCategory.VmImageTransfer, cancellationToken);
        var source = Path.Combine(Path.GetFullPath(_config.ImageStoragePath), request.VmName + ".qcow2");
        if (!File.Exists(source))
            throw Failure("scenario_overlay_missing", "Scenario VM overlay was not found.");
        var workspace = Path.Combine(Path.GetFullPath(_config.ImageStoragePath), "scenario-commit",
            request.OperationId.ToString("N"));
        var output = Path.Combine(workspace, "scenario.qcow2");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(checkpointRoot);
        try
        {
            var isShutOff = await IsShutOffAsync(request.VmName, cancellationToken);
            if (!isShutOff)
            {
                await SanitizeAsync(request, cancellationToken);
                await File.WriteAllTextAsync(sanitized, request.BuildIdentity, cancellationToken);
                await guestAgent.ShutdownAsync(request.VmName, cancellationToken);
                await WaitForShutdownAsync(request.VmName, TimeSpan.FromMinutes(3), cancellationToken);
            }
            else if (!File.Exists(sanitized))
            {
                throw Failure("scenario_sanitation_checkpoint_missing",
                    "Scenario VM is shut off without a sanitation checkpoint.");
            }
            if (!File.Exists(output))
                await RunAsync("qemu-img", ["convert", "-p", "-O", "qcow2", source, output],
                    TimeSpan.FromMinutes(30), cancellationToken);
            await RunAsync("qemu-img", ["check", "-q", output], TimeSpan.FromMinutes(10), cancellationToken);
            var uploaded = await uploader.UploadAsync(
                output,
                registryTarget,
                annotations,
                cancellationToken);
            TryDeleteFile(sanitized);
            return BuildResponse(request, uploaded);
        }
        finally
        {
            try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); } catch { }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static IReadOnlyDictionary<string, string> BuildAnnotations(CommitVmScenarioRequest request) =>
        new Dictionary<string, string>
        {
            ["org.gzctf.scenario.operation"] = request.OperationId.ToString("D"),
            ["org.gzctf.scenario.identity"] = request.BuildIdentity,
            ["org.gzctf.scenario.vm"] = request.VmName
        };

    private static CommitVmScenarioResponse BuildResponse(
        CommitVmScenarioRequest request,
        AgentOciUploadResult uploaded)
    {
        var evidence = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.OperationId,
            request.BuildIdentity,
            uploaded.LayerDigest,
            uploaded.Size,
            uploaded.ManifestDigest
        })));
        return new CommitVmScenarioResponse(
            true,
            uploaded.LayerDigest,
            uploaded.Size,
            evidence,
            request.RegistryTarget.RegistryAddress,
            request.RegistryTarget.Repository,
            request.RegistryTarget.Tag);
    }

    private static async Task<bool> IsShutOffAsync(string vmName, CancellationToken cancellationToken)
    {
        var state = await RunCaptureAsync("virsh", ["domstate", vmName], cancellationToken, false);
        return state.ExitCode != 0 || state.Output.Contains("shut off", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SanitizeAsync(CommitVmScenarioRequest request, CancellationToken cancellationToken)
    {
        var command = request.OsType == VmInitOsType.Windows
            ? new VmGuestCommandRequest(
                "scenario-sanitize",
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command",
                    "Stop-Service GZCTFGuestSupervisor -ErrorAction SilentlyContinue; " +
                    "Remove-Item -Recurse -Force 'C:\\ProgramData\\GZCTF\\GuestSupervisor\\state' -ErrorAction SilentlyContinue; " +
                    "Remove-Item -Recurse -Force 'C:\\ProgramData\\GZCTF\\Runtime' -ErrorAction SilentlyContinue; " +
                    "Remove-Item -Force 'C:\\ProgramData\\GZCTF\\GuestSupervisor\\config.json' -ErrorAction SilentlyContinue"],
                120,
                null)
            : new VmGuestCommandRequest(
                "scenario-sanitize",
                "/bin/sh",
                ["-c",
                    "systemctl stop gzctf-guest-supervisor.service 2>/dev/null || true; " +
                    "rm -rf /opt/gzctf/runtime /var/lib/gzctf/guest-supervisor; " +
                    "rm -f /etc/gzctf/guest-supervisor/config.json; sync"],
                120,
                null);
        var result = await guestAgent.ExecuteAsync(request.VmName, command, cancellationToken);
        if (!result.Success)
            throw Failure("scenario_sanitize_failed", "Scenario guest sanitation failed before artifact capture.");
    }

    private static async Task WaitForShutdownAsync(
        string vmName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        while (!deadline.IsCancellationRequested)
        {
            var state = await RunCaptureAsync("virsh", ["domstate", vmName], deadline.Token, false);
            if (state.ExitCode != 0 || state.Output.Contains("shut off", StringComparison.OrdinalIgnoreCase))
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token);
        }
        throw Failure("scenario_shutdown_timeout", "Scenario VM did not shut down before capture.");
    }

    private static async Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var result = await RunCaptureAsync(fileName, arguments, deadline.Token, true);
        if (result.ExitCode != 0)
            throw Failure("scenario_artifact_command_failed",
                $"{Path.GetFileName(fileName)} failed with exit code {result.ExitCode}: {Trim(result.Error)}");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool killOnCancel)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)
                            ?? throw Failure("scenario_artifact_process_start_failed", $"Failed to start {fileName}.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await stdout, await stderr);
        }
        catch when (killOnCancel)
        {
            try { process.Kill(true); } catch { }
            throw;
        }
    }

    private static void Validate(CommitVmScenarioRequest request)
    {
        if (request.OperationId == Guid.Empty || !VmNamePattern().IsMatch(request.VmName) ||
            string.IsNullOrWhiteSpace(request.BuildIdentity) || request.BuildIdentity.Length > 128)
            throw Failure("scenario_commit_request_invalid", "Scenario artifact request is invalid.");
    }

    private static string Trim(string value) => value.Length <= 2048 ? value : value[^2048..];
    private static AgentOperationException Failure(string code, string message) =>
        new("ImageBuild", code, message, false);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex VmNamePattern();
}
