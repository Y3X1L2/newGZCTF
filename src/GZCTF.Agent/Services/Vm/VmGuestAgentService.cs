using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.Vm;

public sealed partial class VmGuestAgentService(ILogger<VmGuestAgentService> logger) : IVmGuestAgentClient
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const int QgaRpcTimeoutSeconds = 30;
    private const int FileChunkSize = 48 * 1024;
    private const int MaxCapturedOutputBytes = 1024 * 1024;

    public async Task<VmGuestStatusResponse> WaitReadyAsync(
        string vmName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ValidateVmName(vmName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                using var ping = await SendAsync(vmName, "guest-ping", null, deadline.Token);
                using var info = await TrySendAsync(vmName, "guest-info", null, deadline.Token);
                var version = info is null ||
                              !info.RootElement.TryGetProperty("return", out var guestInfo)
                    ? null
                    : ReadString(guestInfo, "version");
                return new VmGuestStatusResponse(true, "QEMU guest agent is ready.", version);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or OperationCanceledException &&
                !cancellationToken.IsCancellationRequested)
            {
                if (deadline.IsCancellationRequested)
                    break;
                await Task.Delay(PollInterval, deadline.Token);
            }
        }

        return new VmGuestStatusResponse(false, "QEMU guest agent did not become ready before the deadline.");
    }

    public async Task WriteFileAsync(
        string vmName,
        string guestPath,
        Stream content,
        CancellationToken cancellationToken)
    {
        ValidateVmName(vmName);
        using var open = await SendAsync(vmName, "guest-file-open", new Dictionary<string, object?>
        {
            ["path"] = guestPath,
            ["mode"] = "wb"
        }, cancellationToken);
        var handle = ReadInt64(open.RootElement, "return")
                     ?? throw new InvalidOperationException("QGA guest-file-open returned no handle.");
        try
        {
            var buffer = new byte[FileChunkSize];
            while (true)
            {
                var read = await content.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                using var write = await SendAsync(vmName, "guest-file-write", new Dictionary<string, object?>
                {
                    ["handle"] = handle,
                    ["buf-b64"] = Convert.ToBase64String(buffer, 0, read)
                }, cancellationToken);
            }

            using var flush = await SendAsync(vmName, "guest-file-flush", new Dictionary<string, object?>
            {
                ["handle"] = handle
            }, cancellationToken);
        }
        finally
        {
            using var close = await TrySendAsync(vmName, "guest-file-close", new Dictionary<string, object?>
            {
                ["handle"] = handle
            }, CancellationToken.None);
        }
    }

    public async Task<bool> TryFileExistsAsync(string vmName, string guestPath, CancellationToken cancellationToken)
    {
        ValidateVmName(vmName);
        cancellationToken.ThrowIfCancellationRequested();
        JsonDocument opened;
        try
        {
            opened = await SendAsync(vmName, "guest-file-open", new Dictionary<string, object?>
            {
                ["path"] = guestPath, ["mode"] = "rb"
            }, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsOptionalFileProbeUnavailableError(exception.Message))
        {
            return false;
        }
        using var open = opened;
        var handle = ReadInt64(open.RootElement, "return")
            ?? throw new InvalidOperationException("QGA guest-file-open returned no handle.");
        // The probe never reads executable bytes. Always close the QGA handle, even on cancellation.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var close = await SendAsync(vmName, "guest-file-close", new Dictionary<string, object?>
        {
            ["handle"] = handle
        }, cleanup.Token);
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    internal static bool IsOptionalFileProbeUnavailableError(string message) =>
        message.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("cannot find the path", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("CommandNotFound", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("CommandDisabled", StringComparison.OrdinalIgnoreCase);

    public async Task<byte[]> ReadFileAsync(
        string vmName,
        string guestPath,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ValidateVmName(vmName);
        using var open = await SendAsync(vmName, "guest-file-open", new Dictionary<string, object?>
        {
            ["path"] = guestPath,
            ["mode"] = "rb"
        }, cancellationToken);
        var handle = ReadInt64(open.RootElement, "return")
                     ?? throw new InvalidOperationException("QGA guest-file-open returned no handle.");
        try
        {
            using var output = new MemoryStream();
            while (output.Length < maxBytes)
            {
                using var response = await SendAsync(vmName, "guest-file-read", new Dictionary<string, object?>
                {
                    ["handle"] = handle,
                    ["count"] = Math.Min(FileChunkSize, maxBytes - (int)output.Length)
                }, cancellationToken);
                var result = response.RootElement.GetProperty("return");
                var payload = result.TryGetProperty("buf-b64", out var data)
                    ? Convert.FromBase64String(data.GetString() ?? string.Empty)
                    : [];
                await output.WriteAsync(payload, cancellationToken);
                if (result.TryGetProperty("eof", out var eof) && eof.GetBoolean())
                    return output.ToArray();
                if (payload.Length == 0)
                    return output.ToArray();
            }

            throw new InvalidOperationException($"Guest file exceeded the {maxBytes}-byte read limit.");
        }
        finally
        {
            using var close = await TrySendAsync(vmName, "guest-file-close", new Dictionary<string, object?>
            {
                ["handle"] = handle
            }, CancellationToken.None);
        }
    }

    public Task<VmGuestCommandResponse> ExecuteAsync(
        string vmName,
        VmGuestCommandRequest command,
        CancellationToken cancellationToken) => ExecuteCoreAsync(vmName, command, false, cancellationToken, null);

    Task<VmGuestCommandResponse> IVmGuestAgentClient.ExecuteAsync(string vmName,
        VmGuestCommandRequest command, CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? verifyIdentity) =>
        ExecuteCoreAsync(vmName, command, true, cancellationToken, verifyIdentity);

    async Task<VmGuestCommandResponse> ExecuteCoreAsync(string vmName,
        VmGuestCommandRequest command, bool terminateOnDeadline, CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? verifyIdentity)
    {
        ValidateVmName(vmName);
        if (string.IsNullOrWhiteSpace(command.StepId) || string.IsNullOrWhiteSpace(command.Path) ||
            command.TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentException("Guest command is invalid.", nameof(command));

        JsonDocument executeResponse;
        try
        {
            executeResponse = await SendAsync(vmName, "guest-exec", BuildGuestExecArguments(command), cancellationToken);
        }
        catch (InvalidOperationException exception) when (command.StandardInput is not null && IsInputDataUnsupportedError(exception.Message))
        {
            return new(false, false, null, "stdin-unavailable", null, "GZCTF_GUEST_STDIN_UNAVAILABLE");
        }
        using var execute = executeResponse;
        var pid = ReadInt64(execute.RootElement.GetProperty("return"), "pid")
                  ?? throw new InvalidOperationException("QGA guest-exec returned no process id.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(command.TimeoutSeconds));
        try
        {
            while (true)
            {
                using var status = await SendAsync(vmName, "guest-exec-status", new Dictionary<string, object?>
                {
                    ["pid"] = pid
                }, deadline.Token);
                var result = status.RootElement.GetProperty("return");
                if (result.TryGetProperty("exited", out var exited) && exited.GetBoolean())
                {
                    var exitCode = result.TryGetProperty("exitcode", out var code) ? (int?)code.GetInt32() : null;
                    var stdout = DecodeCapturedOutput(result, "out-data");
                    var stderr = DecodeCapturedOutput(result, "err-data");
                    return new VmGuestCommandResponse(
                        exitCode == 0,
                        false,
                        exitCode,
                        exitCode == 0 ? "succeeded" : "non-zero-exit",
                        stdout,
                        stderr);
                }

                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (terminateOnDeadline) await TryTerminateAsync(vmName, pid, command.Path, verifyIdentity);
            logger.LogWarning("Guest command timed out: VM={VmName}, Step={StepId}", vmName, command.StepId);
            return new VmGuestCommandResponse(false, true, null, "timeout", null, null);
        }
        catch (OperationCanceledException)
        {
            if (terminateOnDeadline) await TryTerminateAsync(vmName, pid, command.Path, verifyIdentity);
            throw;
        }
    }

    async Task TryTerminateAsync(string vmName, long pid, string commandPath,
        Func<CancellationToken, Task<bool>>? verifyIdentity)
    {
        if (pid <= 0) return;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var windows = IsWindowsCommandPath(commandPath);
        try
        {
            if (verifyIdentity is not null && !await verifyIdentity(cleanup.Token))
            {
                logger.LogWarning("Guest command cleanup skipped because VM native identity changed: VM={VmName}", vmName);
                return;
            }
            using var result = await TrySendAsync(vmName, "guest-exec", new Dictionary<string, object?>
            {
                ["path"] = windows ? @"C:\Windows\System32\taskkill.exe" : "/bin/kill",
                ["arg"] = windows ? new[] { "/PID", pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "/T", "/F" }
                    : new[] { "-TERM", pid.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                ["capture-output"] = false
            }, cleanup.Token);
            while (!cleanup.IsCancellationRequested)
            {
                using var status = await TrySendAsync(vmName, "guest-exec-status", new Dictionary<string, object?>
                {
                    ["pid"] = pid
                }, cleanup.Token);
                if (status is not null && status.RootElement.TryGetProperty("return", out var body) &&
                    body.TryGetProperty("exited", out var exited) && exited.GetBoolean()) return;
                await Task.Delay(PollInterval, cleanup.Token);
            }
        }
        catch (Exception)
        {
            logger.LogWarning("Unable to confirm timed-out guest network command termination: VM={VmName}", vmName);
        }
    }

    internal static bool IsWindowsCommandPath(string commandPath) =>
        commandPath.StartsWith("C:\\Windows\\", StringComparison.OrdinalIgnoreCase) ||
        commandPath.Equals(TeamLabVmNetworkService.WindowsLegacyPowerShellHostPath, StringComparison.OrdinalIgnoreCase);

    public async Task RebootAndWaitAsync(
        string vmName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ValidateVmName(vmName);
        using var shutdown = await TrySendAsync(vmName, "guest-shutdown", new Dictionary<string, object?>
        {
            ["mode"] = "reboot"
        }, cancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var disconnected = false;
        while (!deadline.IsCancellationRequested)
        {
            using var ping = await TrySendAsync(vmName, "guest-ping", null, deadline.Token);
            if (ping is null)
            {
                disconnected = true;
                break;
            }
            await Task.Delay(PollInterval, deadline.Token);
        }
        if (!disconnected)
            throw new InvalidOperationException("Guest reboot did not disconnect the QGA session.");

        var ready = await WaitReadyAsync(vmName, timeout, deadline.Token);
        if (!ready.Ready)
            throw new InvalidOperationException(ready.Message);
    }

    public async Task ShutdownAsync(string vmName, CancellationToken cancellationToken)
    {
        ValidateVmName(vmName);
        using var response = await TrySendAsync(vmName, "guest-shutdown", new Dictionary<string, object?>
        {
            ["mode"] = "powerdown"
        }, cancellationToken);
    }

    async Task<JsonDocument> SendAsync(
        string vmName,
        string command,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunVirshAsync(vmName, command, arguments, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"QGA command {command} failed for VM {vmName}: {Trim(result.Error)}");
        var document = JsonDocument.Parse(result.Output);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            document.Dispose();
            throw new InvalidOperationException($"QGA command {command} failed for VM {vmName}: {Trim(error.ToString())}");
        }
        return document;
    }

    async Task<JsonDocument?> TrySendAsync(
        string vmName,
        string command,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync(vmName, command, arguments, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    static async Task<VirshResult> RunVirshAsync(
        string vmName,
        string command,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        var payload = BuildCommandPayload(command, arguments);
        var info = new ProcessStartInfo
        {
            FileName = "virsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("qemu-agent-command");
        info.ArgumentList.Add(vmName);
        info.ArgumentList.Add("--timeout");
        info.ArgumentList.Add(QgaRpcTimeoutSeconds.ToString());
        info.ArgumentList.Add(payload);
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("Unable to start virsh.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and the kill request.
            }
            throw;
        }
        return new VirshResult(process.ExitCode, await output, await error);
    }

    internal static string BuildCommandPayload(
        string command,
        IReadOnlyDictionary<string, object?>? arguments) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["execute"] = command,
            ["arguments"] = arguments
        }.Where(item => item.Value is not null).ToDictionary());

    internal const int MaxStandardInputBytes = 64 * 1024;

    internal static IReadOnlyDictionary<string, object?> BuildGuestExecArguments(VmGuestCommandRequest command)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["path"] = command.Path, ["arg"] = command.Arguments,
            ["capture-output"] = true
        };
        // QGA distinguishes omitted env (inherit) from [] (empty process environment).
        if (command.Environment is { } environment)
            arguments["env"] = environment.Select(item => $"{item.Key}={item.Value}").ToArray();
        if (command.StandardInput is { } input)
        {
            if (Encoding.UTF8.GetByteCount(input) > MaxStandardInputBytes)
                throw new ArgumentException("Guest command standard input exceeds its byte limit.", nameof(command));
            arguments["input-data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
        }
        return arguments;
    }

    internal static bool IsInputDataUnsupportedError(string message) =>
        message.Contains("input-data", StringComparison.OrdinalIgnoreCase) &&
        (message.Contains("unexpected", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("not supported", StringComparison.OrdinalIgnoreCase));

    static string? DecodeCapturedOutput(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
            return null;
        var bytes = Convert.FromBase64String(value.GetString()!);
        if (bytes.Length > MaxCapturedOutputBytes)
            bytes = bytes[..MaxCapturedOutputBytes];
        return Encoding.UTF8.GetString(bytes);
    }

    static long? ReadInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : null;

    static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    static string Trim(string value) => value.Length <= 512 ? value : value[..512];

    static void ValidateVmName(string vmName)
    {
        if (!SafeName().IsMatch(vmName))
            throw new ArgumentException("Invalid VM name.", nameof(vmName));
    }

    private sealed record VirshResult(int ExitCode, string Output, string Error);

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
