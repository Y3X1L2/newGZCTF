using System.Net;
using GZCTF.Models.Data;

namespace GZCTF.Services.Fleet;

internal static class TeamLabDataPlaneSyncConfiguration
{
    public static TeamLabDataPlaneSyncConfig Create(WorkerNode node, WorkerNode? controlPlaneNode)
    {
        var controlAddress = ParseAddress(controlPlaneNode?.TeamLabTunnelIp);
        var chassisAddress = ParseAddress(node.TeamLabTunnelIp);
        var enabled = node.TeamLabNetworkEnabled;
        var controlPlane = enabled && controlPlaneNode?.Id == node.Id &&
                           controlAddress is not null && chassisAddress is not null;
        var remoteControllerReachable = controlAddress is not null && chassisAddress is not null;
        return new TeamLabDataPlaneSyncConfig(
            enabled,
            controlPlane,
            controlPlane ? "unix:/var/run/ovn/ovnnb_db.sock" :
            remoteControllerReachable ? Endpoint(controlAddress, 6641) : null,
            controlPlane ? "unix:/var/run/ovn/ovnsb_db.sock" :
            remoteControllerReachable ? Endpoint(controlAddress, 6642) : null,
            controlPlane ? PassiveEndpoint(controlAddress, 6641) : null,
            controlPlane ? PassiveEndpoint(controlAddress, 6642) : null,
            chassisAddress,
            "br-int");
    }

    private static string? ParseAddress(string? value) =>
        IPAddress.TryParse(value, out var address) ? address.ToString() : null;

    private static string? Endpoint(string? address, int port) =>
        address is null ? null : $"tcp:{address}:{port}";

    private static string? PassiveEndpoint(string? address, int port) =>
        address is null ? null : $"ptcp:{port}:{address}";
}
