using System.Net;
using GZCTF.Models.Data;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Services.Fleet;

internal static class TeamLabDataPlaneSyncConfiguration
{
    public static TeamLabDataPlaneSyncConfig Create(WorkerNode node, WorkerNode? controlPlaneNode,
        TeamLabExecutionModel executionModel, int managedDhcpLeaseSeconds = 3600)
    {
        var controlAddress = ParseAddress(controlPlaneNode?.TeamLabTunnelIp);
        var chassisAddress = ParseAddress(node.TeamLabTunnelIp);
        var requested = node.TeamLabNetworkEnabled;
        var controlPlane = requested && controlPlaneNode?.Id == node.Id &&
                           controlAddress is not null && chassisAddress is not null;
        var remoteControllerReachable = controlAddress is not null && chassisAddress is not null;
        // An unprovisioned node must receive the Agent and its local prerequisites before it
        // can establish Fabric. It remains ineligible for TeamLab placement until the normal
        // tunnel health projection confirms that Fabric exists.
        var enabled = requested && (controlPlane || remoteControllerReachable);
        return new TeamLabDataPlaneSyncConfig(
            enabled,
            executionModel,
            controlPlane,
            controlPlane ? "unix:/var/run/ovn/ovnnb_db.sock" :
            remoteControllerReachable ? Endpoint(controlAddress, 6641) : null,
            controlPlane ? "unix:/var/run/ovn/ovnsb_db.sock" :
            remoteControllerReachable ? Endpoint(controlAddress, 6642) : null,
            controlPlane ? PassiveEndpoint(controlAddress, 6641) : null,
            controlPlane ? PassiveEndpoint(controlAddress, 6642) : null,
            chassisAddress,
            "br-int",
            Math.Clamp(managedDhcpLeaseSeconds, 60, 86_400));
    }

    private static string? ParseAddress(string? value) =>
        IPAddress.TryParse(value, out var address) ? address.ToString() : null;

    private static string? Endpoint(string? address, int port) =>
        address is null ? null : $"tcp:{address}:{port}";

    private static string? PassiveEndpoint(string? address, int port) =>
        address is null ? null : $"ptcp:{port}:{address}";
}
