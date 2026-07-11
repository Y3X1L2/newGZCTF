using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public sealed class ProcessExecutionTests
{
    [Fact]
    public async Task RunAsync_TimeoutKillsProcessTreeBeforeDelayedSideEffect()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"gzctf-process-{Guid.NewGuid():N}.txt");
        var (fileName, arguments) = BuildDelayedWriteCommand(marker);

        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                ProcessExecution.RunAsync(
                    fileName,
                    arguments,
                    TimeSpan.FromMilliseconds(150),
                    CancellationToken.None));

            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    private static (string FileName, IReadOnlyList<string> Arguments) BuildDelayedWriteCommand(string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            var escaped = marker.Replace("'", "''", StringComparison.Ordinal);
            return ("powershell.exe",
                ["-NoProfile", "-Command", $"Start-Sleep -Seconds 2; Set-Content -LiteralPath '{escaped}' -Value done"]);
        }

        var shellPath = marker.Replace("'", "'\"'\"'", StringComparison.Ordinal);
        return ("/bin/sh", ["-c", $"sleep 2; printf done > '{shellPath}'"]);
    }
}
