using System.Diagnostics;

namespace GZCTF.Services;

internal static class ProcessExecution
{
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(10);

    public static async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            await TerminateAsync(process);
            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new TimeoutException(
                $"Process '{fileName}' exceeded its {timeout.TotalSeconds:0}-second timeout.");
        }

        return new ProcessExecutionResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static async Task TerminateAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        using var terminationSource = new CancellationTokenSource(TerminationTimeout);
        try
        {
            await process.WaitForExitAsync(terminationSource.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("The cancelled process did not terminate after it was killed.");
        }
    }
}

internal readonly record struct ProcessExecutionResult(
    int ExitCode,
    string Output,
    string Error);
