using System;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.RemoteAccess;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabTerminalSessionRegistryTests
{
    [Fact]
    public async Task FailureBeforeProcessCreation_DoesNotLeaveCleanupPending()
    {
        var registry = new TeamLabTerminalSessionRegistry();
        var id = Guid.NewGuid();
        registry.Attach(id, DateTimeOffset.UtcNow.AddMinutes(5));
        registry.Detach(id, new InvalidOperationException("terminal unsupported"));

        await registry.CancelAndWaitAsync(id);

        Assert.Empty(registry.ActiveSessionIds());
        Assert.Throws<AgentOperationException>(() => registry.Attach(id, DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task ExpiredSession_PreservesFailedCleanupUntilRetrySucceeds()
    {
        var clock = new TestClock();
        var registry = new TeamLabTerminalSessionRegistry(clock);
        var id = Guid.NewGuid();
        registry.Attach(id, clock.GetUtcNow().AddMinutes(5));
        var calls = 0;
        registry.RegisterCleanup(id, _ => { calls++; return Task.CompletedTask; });
        registry.Detach(id, new InvalidOperationException("cleanup failed"));
        clock.Now = clock.Now.AddDays(1);

        await registry.CancelAndWaitAsync(id);

        Assert.Equal(1, calls);
        await registry.CancelAndWaitAsync(id);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExpiredAttachedSession_CancellationStillWaitsForExecution()
    {
        var clock = new TestClock();
        var registry = new TeamLabTerminalSessionRegistry(clock);
        var id = Guid.NewGuid();
        var token = registry.Attach(id, clock.GetUtcNow().AddMinutes(5));
        clock.Now = clock.Now.AddDays(1);

        var ending = registry.CancelAndWaitAsync(id);

        Assert.True(token.IsCancellationRequested);
        Assert.False(ending.IsCompleted);
        registry.Detach(id);
        await ending;
    }

    [Fact]
    public async Task ConcurrentCancellation_RunsSuccessfulCleanupOnlyOnce()
    {
        var registry = new TeamLabTerminalSessionRegistry();
        var id = Guid.NewGuid();
        registry.Attach(id, DateTimeOffset.UtcNow.AddMinutes(5));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        registry.RegisterCleanup(id, async ct => { calls++; await release.Task.WaitAsync(ct); });
        registry.Detach(id, new InvalidOperationException("cleanup failed"));

        var first = registry.CancelAndWaitAsync(id);
        var second = registry.CancelAndWaitAsync(id);
        Assert.Equal(1, calls);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, calls);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task CancelAndWait_DoesNotAcknowledgeUntilExecutionDetaches()
    {
        var registry = new TeamLabTerminalSessionRegistry();
        var id = Guid.NewGuid();
        registry.Attach(id, DateTimeOffset.UtcNow.AddMinutes(5));
        var ending = registry.CancelAndWaitAsync(id);
        Assert.False(ending.IsCompleted);
        registry.Detach(id);
        await ending;
        Assert.Throws<AgentOperationException>(() => registry.Attach(id, DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task CancelAndWait_RetriesFailedCleanupAndPropagatesFailure()
    {
        var registry = new TeamLabTerminalSessionRegistry();
        var id = Guid.NewGuid();
        registry.Attach(id, DateTimeOffset.UtcNow.AddMinutes(5));
        registry.RegisterCleanup(id, _ => Task.FromException(new InvalidOperationException("cleanup unavailable")));
        registry.Detach(id, new InvalidOperationException("first failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.CancelAndWaitAsync(id));
        var calls = 0;
        registry.RegisterCleanup(id, _ => { calls++; return Task.CompletedTask; });
        await registry.CancelAndWaitAsync(id);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancelBeforeAttach_PreventsLateCreationWithoutWaiting()
    {
        var registry = new TeamLabTerminalSessionRegistry();
        var id = Guid.NewGuid();
        await registry.CancelAndWaitAsync(id);
        Assert.Throws<AgentOperationException>(() => registry.Attach(id, DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public void Cancel_StopsAttachedTerminalAndPreventsLateReconnect()
    {
        var registry = new TeamLabTerminalSessionRegistry();
        var sessionId = Guid.CreateVersion7();

        var token = registry.Attach(sessionId, DateTimeOffset.UtcNow.AddMinutes(5));
        registry.Cancel(sessionId);

        Assert.True(token.IsCancellationRequested);
        var error = Assert.Throws<AgentOperationException>(() => registry.Attach(sessionId, DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Equal("remote_access.terminal_ended", error.Code);
    }

    [Fact]
    public void Attach_RejectsASecondLiveConnection()
    {
        var registry = new TeamLabTerminalSessionRegistry();
        var sessionId = Guid.CreateVersion7();
        registry.Attach(sessionId, DateTimeOffset.UtcNow.AddMinutes(5));

        var error = Assert.Throws<AgentOperationException>(() => registry.Attach(sessionId, DateTimeOffset.UtcNow.AddMinutes(5)));

        Assert.Equal("remote_access.terminal_connected", error.Code);
    }
}
