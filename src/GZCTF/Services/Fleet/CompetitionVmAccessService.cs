using System.Net.Sockets;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Vm;

namespace GZCTF.Services.Fleet;

public sealed record CompetitionVmRdpEndpoint(string IpAddress, string Host, int Port);

public sealed record CompetitionVmRdpAccess(
    string IpAddress,
    string Host,
    int Port,
    string Username,
    string Password);

public sealed class CompetitionVmAccessService(
    ImageRemoteAccessService imageRemoteAccess,
    INodeRepository nodeRepository,
    AgentClient agentClient,
    IVirtualMachineProvider vmProvider)
{
    public Task<CompetitionWindowsRdpProfile?> GetImageProfileAsync(
        int imageTemplateId,
        CancellationToken cancellationToken) =>
        imageRemoteAccess.GetCompetitionWindowsRdpProfileAsync(imageTemplateId, cancellationToken);

    public async Task<CompetitionVmRdpEndpoint?> GetEndpointAsync(
        VmInstance vm,
        int targetPort,
        CancellationToken cancellationToken)
    {
        var node = vm.NodeId.HasValue
            ? await nodeRepository.GetNodeByIdAsync(vm.NodeId.Value, cancellationToken)
            : null;
        if (node is null || node.IsLocal)
        {
            var ipAddress = vm.IpAddress ?? await vmProvider.GetIpAddressAsync(vm.VmName, cancellationToken);
            if (string.IsNullOrWhiteSpace(ipAddress) ||
                !await IsTcpPortReadyAsync(ipAddress, targetPort, cancellationToken))
                return null;
            return new CompetitionVmRdpEndpoint(ipAddress, ipAddress, targetPort);
        }

        var response = await agentClient.GetVmIpAsync(
            node.Id,
            vm.VmName,
            targetPort,
            vm.RuntimeGeneration,
            vm.RuntimeNativeId,
            cancellationToken);
        return response is
        {
            Status: "Ready",
            IpAddress: not null,
            RdpPort: not null
        }
            ? new CompetitionVmRdpEndpoint(response.IpAddress, node.HostAddress, response.RdpPort.Value)
            : null;
    }

    public async Task<CompetitionVmRdpAccess?> GetAccessAsync(
        VmInstance vm,
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var profile = await GetImageProfileAsync(imageTemplateId, cancellationToken);
        if (profile is null)
            return null;
        var endpoint = await GetEndpointAsync(vm, profile.Port, cancellationToken);
        return endpoint is null
            ? null
            : new CompetitionVmRdpAccess(
                endpoint.IpAddress,
                endpoint.Host,
                endpoint.Port,
                profile.Username,
                profile.Password);
    }

    private static async Task<bool> IsTcpPortReadyAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
