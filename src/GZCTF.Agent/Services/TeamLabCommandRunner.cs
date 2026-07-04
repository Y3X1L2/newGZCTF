using System.Diagnostics;

namespace GZCTF.Agent.Services;

public class TeamLabCommandRunner(ILogger<TeamLabCommandRunner> logger)
{
    internal const string StableWorkingDirectory = "/";

    public virtual Task<(bool Success, string Output)> RunAsync(string command, CancellationToken token) =>
        RunAsync(command, null, token);

    public virtual async Task<(bool Success, string Output)> RunAsync(string command, string? standardInput,
        CancellationToken token)
    {
        var startInfo = CreateStartInfo(command, standardInput is not null);

        using var process = Process.Start(startInfo);
        if (process is null)
            return (false, "Failed to start shell process.");

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            await process.StandardInput.FlushAsync(token);
            process.StandardInput.Close();
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(token);
        var stderr = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);

        var output = string.Join('\n', new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (process.ExitCode != 0)
            logger.LogWarning("TeamLab command failed with exit code {ExitCode}: {Command}\n{Output}",
                process.ExitCode, command, output);

        return (process.ExitCode == 0, output);
    }

    internal static ProcessStartInfo CreateStartInfo(string command, bool redirectStandardInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
            WorkingDirectory = StableWorkingDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }
}
