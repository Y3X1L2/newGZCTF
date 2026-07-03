using System.Diagnostics;

namespace GZCTF.Agent.Services;

public class TeamLabCommandRunner(ILogger<TeamLabCommandRunner> logger)
{
    public async Task<(bool Success, string Output)> RunAsync(string command, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { "-c", command },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        if (process is null)
            return (false, "Failed to start shell process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(token);
        var stderr = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);

        var output = string.Join('\n', new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (process.ExitCode != 0)
            logger.LogWarning("TeamLab command failed with exit code {ExitCode}: {Command}\n{Output}",
                process.ExitCode, command, output);

        return (process.ExitCode == 0, output);
    }
}
