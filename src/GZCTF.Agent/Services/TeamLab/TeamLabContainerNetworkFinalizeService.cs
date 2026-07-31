using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.RuntimeSignals;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed partial class TeamLabContainerNetworkFinalizeService(
    DockerService docker,
    TeamLabCommandRunner runner,
    TeamLabRuntimeGenerationStore generationStore,
    AgentResourceLock resourceLock,
    AgentRuntimeSignalPublisher signals,
    IOptions<AgentTeamLabConfig> options,
    ILogger<TeamLabContainerNetworkFinalizeService> logger)
{
    private readonly AgentTeamLabConfig _config = options.Value;

    public async Task<TeamLabContainerNetworkFinalizeResponse> FinalizeAsync(
        TeamLabContainerNetworkFinalizeRequest request,
        CancellationToken token)
    {
        var validation = Validate(request);
        if (validation is not null) return Failure(validation, request.DryRun);

        await using var runtimeLock = await resourceLock.AcquireAsync(
            TeamLabNetworkService.RuntimeLockKey(request.RuntimeId), token);
        TeamLabActiveGeneration? active;
        try
        {
            active = await generationStore.ReadAsync(request.RuntimeId, token);
        }
        catch (InvalidDataException exception)
        {
            return Failure(exception.Message, request.DryRun);
        }
        if (active?.Generation != request.Generation)
            return Failure(
                $"Container network finalization generation {request.Generation} is not active for runtime {request.RuntimeId}.",
                request.DryRun);

        await using var containerLock = await resourceLock.AcquireAsync(
            $"container:{request.ContainerName}", token);
        var identity = await docker.InspectTeamLabContainerAsync(request.ContainerId, token);
        if (identity is null)
            return Failure("Container was not found.", request.DryRun);
        if (!string.Equals(identity.ContainerId, request.ContainerId, StringComparison.Ordinal) ||
            !string.Equals(identity.ContainerName, request.ContainerName, StringComparison.Ordinal) ||
            identity.RuntimeId != request.RuntimeId ||
            identity.Generation != request.Generation)
            return Failure("Container identity does not match the requested runtime generation.", request.DryRun);
        if (!identity.Running || identity.Pid <= 0)
            return Failure("Container is not running in the requested runtime generation.", request.DryRun);

        var command = BuildFinalizeCommand(identity.Pid, request);
        if (!_config.Enable || _config.DryRun || request.DryRun)
            return new TeamLabContainerNetworkFinalizeResponse(
                true, true, "Container network finalization command plan returned without execution.", false, [command]);
        if (!TeamLabNetworkPrimitives.HasCommand("dig"))
            return Failure("Missing DNS response probe dependency: dig.", request.DryRun);

        var result = await runner.RunAsync(command, token);
        if (!result.Success)
        {
            logger.LogWarning(
                "TeamLab container network finalization failed: runtime={RuntimeId}, generation={Generation}, container={ContainerId}, detail={Detail}",
                request.RuntimeId, request.Generation, request.ContainerId, result.Output);
            return new TeamLabContainerNetworkFinalizeResponse(
                false, false, result.Output, false, [command]);
        }

        await signals.AppendAsync(new AgentRuntimeSignalDraft(
            request.OperationId,
            request.RuntimeId,
            request.Generation,
            "container",
            request.ContainerId,
            AgentRuntimeSignalStage.NetworkReady,
            AgentRuntimeSignalOutcome.Ready,
            Facts: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["containerName"] = request.ContainerName,
                ["interfaceCount"] = request.Interfaces.Length.ToString(),
                ["dnsProbeCount"] = request.DnsProbes.Length.ToString()
            }), token);

        return new TeamLabContainerNetworkFinalizeResponse(
            true,
            false,
            "Container network facts and DNS responses verified; startup gate released.",
            result.Output.Contains("finalized:1", StringComparison.Ordinal),
            [command]);
    }

    internal static string BuildFinalizeCommand(
        long pid,
        TeamLabContainerNetworkFinalizeRequest request)
    {
        var marker = $"/proc/{pid}/root/tmp/.gzctf-teamlab-network-ready";
        var containerId = TeamLabNetworkPrimitives.ShellQuote(request.ContainerId);
        var checks = new List<string>
        {
            "set -eu",
            "command -v docker >/dev/null 2>&1",
            "command -v ip >/dev/null 2>&1",
            "command -v nsenter >/dev/null 2>&1",
            "command -v dig >/dev/null 2>&1",
            $"test \"$(docker inspect -f '{{{{.State.Pid}}}}' {containerId})\" = '{pid}'",
            $"already=0; test ! -f {marker} || already=1"
        };

        foreach (var item in request.Interfaces.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var name = TeamLabNetworkPrimitives.ShellQuote(item.Name);
            var address = TeamLabNetworkPrimitives.ShellQuote(item.AddressCidr);
            checks.Add($"nsenter -t {pid} -n ip link show dev {name} >/dev/null");
            checks.Add(
                $"nsenter -t {pid} -n ip -o -4 addr show dev {name} | awk '{{print $4}}' | grep -Fx {address} >/dev/null");
            checks.Add(
                $"nsenter -t {pid} -n ip -o link show dev {name} | grep -Fi 'link/ether {item.MacAddress} ' >/dev/null");
        }

        foreach (var route in request.Routes
                     .OrderBy(item => item.TargetCidr, StringComparer.Ordinal)
                     .ThenBy(item => item.InterfaceName, StringComparer.Ordinal))
        {
            var command =
                $"nsenter -t {pid} -n ip route show exact {TeamLabNetworkPrimitives.ShellQuote(route.TargetCidr)}";
            if (!string.IsNullOrWhiteSpace(route.GatewayIp))
                command += $" | grep -F 'via {route.GatewayIp}'";
            command += $" | grep -F 'dev {route.InterfaceName}' >/dev/null";
            checks.Add(command);
        }

        if (request.RequireNoDefaultRoute)
            checks.Add($"test -z \"$(nsenter -t {pid} -n ip route show default)\"");

        foreach (var server in request.DnsServers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            checks.Add(
                $"grep -Fx {TeamLabNetworkPrimitives.ShellQuote($"nameserver {server}")} /proc/{pid}/root/etc/resolv.conf >/dev/null");

        foreach (var probe in request.DnsProbes
                     .OrderBy(item => item.Server, StringComparer.Ordinal)
                     .ThenBy(item => item.QueryName, StringComparer.Ordinal))
            checks.Add(
                $"nsenter -t {pid} -n dig +time=2 +tries=1 +short @{TeamLabNetworkPrimitives.ShellQuote(probe.Server)} {TeamLabNetworkPrimitives.ShellQuote(probe.QueryName)} A | grep -Fx {TeamLabNetworkPrimitives.ShellQuote(probe.ExpectedAddress)} >/dev/null");

        checks.Add($"touch {marker}");
        checks.Add("printf 'finalized:%s\\n' \"$already\"");
        return string.Join("; ", checks);
    }

    private TeamLabContainerNetworkFinalizeResponse Failure(string message, bool requestDryRun) =>
        new(false, _config.DryRun || requestDryRun || !_config.Enable, message, false, []);

    private static string? Validate(TeamLabContainerNetworkFinalizeRequest request)
    {
        if (request.OperationId == Guid.Empty) return "Invalid OperationId.";
        if (request.RuntimeId <= 0) return "Invalid RuntimeId.";
        if (request.Generation <= 0) return "Invalid Generation.";
        if (string.IsNullOrWhiteSpace(request.ContainerId) || !ContainerIdRegex().IsMatch(request.ContainerId))
            return "Invalid ContainerId.";
        if (string.IsNullOrWhiteSpace(request.ContainerName) || !ContainerNameRegex().IsMatch(request.ContainerName))
            return "Invalid ContainerName.";
        if (request.Interfaces is null || request.Interfaces.Length == 0)
            return "At least one expected container interface is required.";
        if (request.Routes is null || request.DnsServers is null || request.DnsProbes is null ||
            request.DnsServers.Length == 0 || request.DnsProbes.Length == 0)
            return "Expected routes, DNS servers and at least one DNS response probe are required.";

        foreach (var item in request.Interfaces)
        {
            var validation = TeamLabNetworkPrimitives.ValidateLinuxName(item.Name, nameof(item.Name)) ??
                             TeamLabNetworkPrimitives.ValidateCidr(item.AddressCidr, nameof(item.AddressCidr));
            if (validation is not null) return validation;
            if (string.IsNullOrWhiteSpace(item.MacAddress) || !MacAddressRegex().IsMatch(item.MacAddress))
                return "Invalid interface MacAddress.";
        }
        foreach (var route in request.Routes)
        {
            var validation = TeamLabNetworkPrimitives.ValidateCidr(route.TargetCidr, nameof(route.TargetCidr)) ??
                             TeamLabNetworkPrimitives.ValidateLinuxName(route.InterfaceName,
                                 nameof(route.InterfaceName));
            if (validation is not null) return validation;
            if (!string.IsNullOrWhiteSpace(route.GatewayIp) &&
                TeamLabNetworkPrimitives.ValidateIp(route.GatewayIp, nameof(route.GatewayIp)) is { } gatewayError)
                return gatewayError;
        }
        foreach (var server in request.DnsServers)
        {
            var validation = TeamLabNetworkPrimitives.ValidateIp(server, nameof(request.DnsServers));
            if (validation is not null) return validation;
        }
        foreach (var probe in request.DnsProbes)
        {
            var validation = TeamLabNetworkPrimitives.ValidateIp(probe.Server, nameof(probe.Server)) ??
                             TeamLabNetworkPrimitives.ValidateHostname(probe.QueryName, nameof(probe.QueryName)) ??
                             TeamLabNetworkPrimitives.ValidateIp(probe.ExpectedAddress,
                                 nameof(probe.ExpectedAddress));
            if (validation is not null) return validation;
            if (!request.DnsServers.Contains(probe.Server, StringComparer.Ordinal))
                return "A DNS probe server is not present in the expected DNS server list.";
        }
        return null;
    }

    [GeneratedRegex("^[a-fA-F0-9]{12,64}$")]
    private static partial Regex ContainerIdRegex();

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$")]
    private static partial Regex ContainerNameRegex();

    [GeneratedRegex("^(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$")]
    private static partial Regex MacAddressRegex();
}
