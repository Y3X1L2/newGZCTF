using System;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.RemoteAccess;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabTerminalSessionRegistryTests
{
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
