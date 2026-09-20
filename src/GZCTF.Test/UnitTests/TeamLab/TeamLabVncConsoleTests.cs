using System;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services;
using GZCTF.TeamLab.Contracts;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabVncConsoleTests
{
    [Theory]
    [InlineData("127.0.0.1", "5902", true)]
    [InlineData("0.0.0.0", "5902", false)]
    [InlineData("127.0.0.1", "-1", false)]
    [InlineData("127.0.0.1", "22", false)]
    public void ConsolePortRequiresBoundIdentityAndLoopback(string address, string port, bool accepted)
    {
        var identity = new TeamLabVmDiagnosticsRequest("vm-test", 2, Guid.NewGuid());
        var xml = $"<domain><name>vm-test</name><uuid>{identity.NativeId}</uuid><description>gzctf-generation=2</description><devices><graphics type='vnc' listen='{address}' port='{port}' /></devices></domain>";
        if (accepted) Assert.Equal(5902, KvmService.ParseVncConsolePort(xml, identity));
        else Assert.Throws<AgentOperationException>(() => KvmService.ParseVncConsolePort(xml, identity));
        Assert.Throws<AgentOperationException>(() => KvmService.ParseVncConsolePort(xml, identity with { NativeId = Guid.NewGuid() }));
    }

    [Fact]
    public void ConsoleWorksBeforeGuestReadinessButNotDuringDestruction()
    {
        Assert.True(TeamLabRemoteAccessService.CanOpenSession(TeamLabRemoteProtocol.Vnc, TeamLabRuntimeStatus.Deploying));
        Assert.True(TeamLabRemoteAccessService.CanOpenSession(TeamLabRemoteProtocol.Vnc, TeamLabRuntimeStatus.Paused));
        Assert.False(TeamLabRemoteAccessService.CanOpenSession(TeamLabRemoteProtocol.Ssh, TeamLabRuntimeStatus.Paused));
        Assert.False(TeamLabRemoteAccessService.CanOpenSession(TeamLabRemoteProtocol.Vnc, TeamLabRuntimeStatus.Destroying));
        var connection = GuacamoleService.BuildVncConnectionData("console", "worker", 47001);
        Assert.Equal("vnc", connection.Protocol);
        Assert.False(connection.Parameters.ContainsKey("password"));
        Assert.False(connection.Parameters.ContainsKey("username"));
    }
}
