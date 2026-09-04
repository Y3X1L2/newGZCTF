using System.Text.Json.Nodes;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.TeamLab.Contracts;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class AgentMaintenanceConfigurationTests
{
    [Fact]
    public void TeamLabDataPlaneSync_PersistsEnabledStateAcrossAgentRestart()
    {
        var teamLab = JsonNode.Parse("""
            {
              "Enable": false,
              "ExecutionModel": "V1"
            }
            """)!.AsObject();
        var desired = new TeamLabDataPlaneSyncConfig(
            true,
            TeamLabExecutionModel.V2,
            false,
            "tcp:10.24.0.27:6641",
            "tcp:10.24.0.27:6642",
            null,
            null,
            "10.24.0.31",
            "br-int",
            3600);

        var changed = AgentMaintenanceService.ApplyTeamLabDataPlaneConfig(teamLab, desired);

        Assert.True(changed);
        Assert.True(teamLab["Enable"]!.GetValue<bool>());
        Assert.Equal("V2", teamLab["ExecutionModel"]!.GetValue<string>());
        Assert.Equal("tcp:10.24.0.27:6641", teamLab["OvnNorthboundEndpoint"]!.GetValue<string>());
        Assert.Equal("tcp:10.24.0.27:6642", teamLab["OvnSouthboundEndpoint"]!.GetValue<string>());
        Assert.Equal("br-int", teamLab["OvsIntegrationBridgeName"]!.GetValue<string>());
        Assert.Equal(3600, teamLab["ManagedDhcpLeaseSeconds"]!.GetValue<int>());
        Assert.False(AgentMaintenanceService.ApplyTeamLabDataPlaneConfig(teamLab, desired));
    }
}
