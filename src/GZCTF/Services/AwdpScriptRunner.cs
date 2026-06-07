using System.Diagnostics;
using System.Text;

namespace GZCTF.Services;

public class AwdpScriptRunner(ILogger<AwdpScriptRunner> logger)
{
    const int MaxOutputLength = 1024;
    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<(CheckerStatus Status, string? Message)> RunChecker(AwdpService service,
        AwdpServiceInstance instance, string flag, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(service.CheckerScript))
            return (CheckerStatus.OK, "Checker script is not configured");

        var result = await RunScript(service.CheckerScript, service.CheckerEntrypoint, service, instance, flag,
            DefaultTimeout, token);

        if (result.TimedOut)
            return (CheckerStatus.Down, "Checker execution timed out");

        if (result.ExitCode == 0)
            return (ParseCheckerStatus(result.StandardOutput), BuildMessage(result.StandardOutput, result.StandardError));

        return (CheckerStatus.Down, BuildMessage(result.StandardOutput, result.StandardError) ?? "Checker failed");
    }

    public async Task<AwdpPatchStatus> RunExp(AwdpService service, AwdpServiceInstance instance, string flag,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(service.ExpScript))
            return AwdpPatchStatus.ExpFailed;

        var result = await RunScript(service.ExpScript, service.ExpEntrypoint, service, instance, flag, DefaultTimeout,
            token);

        if (result.TimedOut)
            return AwdpPatchStatus.Timeout;

        return result.ExitCode == 0 ? AwdpPatchStatus.ExpSucceeded : AwdpPatchStatus.ExpFailed;
    }

    async Task<ScriptResult> RunScript(string script, string? entrypoint, AwdpService service,
        AwdpServiceInstance instance, string flag, TimeSpan timeout, CancellationToken token)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gzctf-awdp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        foreach (var fileName in new[] { "script.py", "checker.py", "exp.py" })
            await File.WriteAllTextAsync(Path.Combine(tempDir, fileName), script, Encoding.UTF8, token);

        var command = string.IsNullOrWhiteSpace(entrypoint) ? "python script.py" : entrypoint;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);

        try
        {
            var startInfo = BuildStartInfo(command, tempDir, service, instance, flag);
            using var process = Process.Start(startInfo);

            if (process is null)
                return new(-1, string.Empty, "Failed to start script process", false);

            var stdOutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stdErrTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
                var stdout = await stdOutTask;
                var stderr = await stdErrTask;

                return new(process.ExitCode, Truncate(stdout), Truncate(stderr), false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                _ = TryKill(process);
                return new(-1, string.Empty, "Script timed out", true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AWDP script execution failed for service {ServiceId}, team {TeamId}",
                service.Id, instance.TeamId);
            return new(-1, string.Empty, ex.Message, false);
        }
        finally
        {
            _ = TryDelete(tempDir);
        }
    }

    static ProcessStartInfo BuildStartInfo(string command, string tempDir, AwdpService service,
        AwdpServiceInstance instance, string flag)
    {
        var container = instance.Container;
        var host = container?.PublicIP ?? container?.IP ?? string.Empty;
        var port = (container?.PublicPort ?? container?.Port ?? service.ExposePort).ToString();

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c {command}")
            : new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");

        startInfo.WorkingDirectory = tempDir;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        startInfo.Environment["AWDP_TARGET_HOST"] = host;
        startInfo.Environment["AWDP_TARGET_PORT"] = port;
        startInfo.Environment["AWDP_FLAG"] = flag;
        startInfo.Environment["AWDP_SERVICE_ID"] = service.Id.ToString();
        startInfo.Environment["AWDP_SERVICE_NAME"] = service.Name;
        startInfo.Environment["AWDP_TEAM_ID"] = instance.TeamId.ToString();

        return startInfo;
    }

    static CheckerStatus ParseCheckerStatus(string stdout)
    {
        var output = stdout.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?.Trim().ToUpperInvariant();

        return output switch
        {
            "OK" => CheckerStatus.OK,
            "MUMBLE" => CheckerStatus.Mumble,
            "DOWN" => CheckerStatus.Down,
            "CORRUPT" => CheckerStatus.Corrupt,
            _ => CheckerStatus.Mumble
        };
    }

    static string? BuildMessage(string stdout, string stderr)
    {
        var output = string.Join('\n', new[] { stdout.Trim(), stderr.Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return string.IsNullOrWhiteSpace(output) ? null : Truncate(output);
    }

    static string Truncate(string text) =>
        text.Length <= MaxOutputLength ? text : text[..MaxOutputLength];

    static bool TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static bool TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
}
