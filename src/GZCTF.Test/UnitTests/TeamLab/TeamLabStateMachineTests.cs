using GZCTF.Models.Data;
using GZCTF.Services.TeamLab;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabStateMachineTests
{
    [Theory]
    [InlineData(TeamLabRuntimeStatus.Pending, TeamLabRuntimeStatus.Planning)]
    [InlineData(TeamLabRuntimeStatus.Planning, TeamLabRuntimeStatus.Scheduled)]
    [InlineData(TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Deploying)]
    [InlineData(TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.Probing)]
    [InlineData(TeamLabRuntimeStatus.Probing, TeamLabRuntimeStatus.Running)]
    [InlineData(TeamLabRuntimeStatus.Running, TeamLabRuntimeStatus.Destroying)]
    [InlineData(TeamLabRuntimeStatus.Destroying, TeamLabRuntimeStatus.Destroyed)]
    public void CanTransition_AllowsExpectedRuntimePath(
        TeamLabRuntimeStatus from,
        TeamLabRuntimeStatus to)
    {
        Assert.True(TeamLabStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void CanTransition_DisallowsPartialSuccessToRunning()
    {
        Assert.False(TeamLabStateMachine.CanTransition(TeamLabRuntimeStatus.Failed, TeamLabRuntimeStatus.Running));
        Assert.False(TeamLabStateMachine.CanTransition(TeamLabRuntimeStatus.CleanupPending, TeamLabRuntimeStatus.Running));
    }
}
