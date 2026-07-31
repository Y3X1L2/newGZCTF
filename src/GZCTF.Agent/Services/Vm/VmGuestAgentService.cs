using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.Vm;

public sealed partial class VmGuestAgentService(ILogger<VmGuestAgentService> logger)
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

    public async Task<VmGuestCommandResponse> ExecuteAsync(
        string vmName,
        VmGuestCommandRequest command,
        CancellationToken cancellationToken)
    {
        ValidateVmName(vmName);
        if (string.IsNullOrWhiteSpace(command.StepId) || string.IsNullOrWhiteSpace(command.Path) ||
            command.TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentException("Guest command is invalid.", nameof(command));

        using var execute = await SendAsync(vmName, "guest-exec", new Dictionary<string, object?>
        {
            ["path"] = command.Path,
            ["arg"] = command.Arguments,
            ["env"] = command.Environment?.Select(item => $"{item.Key}={item.Value}").ToArray() ?? [],
            ["capture-output"] = true
        }, cancellationToken);
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
            logger.LogWarning("Guest command timed out: VM={VmName}, Step={StepId}", vmName, command.StepId);
            return new VmGuestCommandResponse(false, true, null, "timeout", null, null);
        }
    }

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
