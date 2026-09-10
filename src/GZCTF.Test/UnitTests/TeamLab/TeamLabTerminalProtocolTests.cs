using System.Text;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabTerminalProtocolTests
{
    [Fact]
    public async Task ResizeDoesNotWriteControlJsonIntoShell()
    {
        var resized = (0, 0);
        await TeamLabTerminalProtocol.ApplyControlAsync(Encoding.UTF8.GetBytes("{\"type\":\"resize\",\"cols\":120,\"rows\":40}"),
            (_, _) => throw new InvalidOperationException("Control data entered shell"),
            (cols, rows, _) => { resized = (cols, rows); return Task.CompletedTask; }, default);
        Assert.Equal((120, 40), resized);
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(501, 24)]
    [InlineData(80, 0)]
    [InlineData(80, 301)]
    public async Task InvalidDimensionsAreRejected(int cols, int rows)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => TeamLabTerminalProtocol.ApplyControlAsync(
            Encoding.UTF8.GetBytes($"{{\"type\":\"resize\",\"cols\":{cols},\"rows\":{rows}}}"),
            (_, _) => Task.CompletedTask, (_, _, _) => throw new InvalidOperationException(), default));
    }

    [Fact]
    public async Task InterruptUsesPtyControlByte()
    {
        byte[]? written = null;
        await TeamLabTerminalProtocol.ApplyControlAsync(Encoding.UTF8.GetBytes("{\"type\":\"signal\",\"signal\":\"INT\"}"),
            (bytes, _) => { written = bytes; return Task.CompletedTask; }, (_, _, _) => Task.CompletedTask, default);
        Assert.Equal(new byte[] { 3 }, written);
    }

    [Fact]
    public async Task MissingKvmDoesNotInvokeUnsupportedCollector()
    {
        var count = await HeartbeatWorker.ReadCountAsync(false,
            _ => throw new InvalidOperationException("virsh is unavailable"), "KVM", NullLogger.Instance, default);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task FailedCollectorDoesNotAbortOtherMetrics()
    {
        Assert.Equal(0, await HeartbeatWorker.ReadCountAsync(true,
            _ => throw new IOException(), "KVM", NullLogger.Instance, default));
        Assert.Equal(3, await HeartbeatWorker.ReadCountAsync(true,
            _ => Task.FromResult(3), "Docker", NullLogger.Instance, default));
    }

    [Fact]
    public async Task HeartbeatCancellationIsNotSuppressed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HeartbeatWorker.ReadCountAsync(false,
            _ => Task.FromResult(0), "KVM", NullLogger.Instance, cancellation.Token));
    }
}
