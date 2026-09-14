using System.Net;
using GZCTF.Agent.Services;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabServiceAccessService(TeamLabCommandRunner runner)
{
    public async Task ApplyAsync(TeamLabServiceForwardRequest request, CancellationToken token)
    {
        Validate(request);
        var comment = $"gzctf-teamlab-service-{request.AccessId:N}";
        var remove = RemoveCommand(comment);
        var commands = new[]
        {
            "nft add table inet gzctf_teamlab 2>/dev/null || true",
            "nft 'add chain inet gzctf_teamlab prerouting { type nat hook prerouting priority dstnat; policy accept; }' 2>/dev/null || true",
            "nft 'add chain inet gzctf_teamlab postrouting { type nat hook postrouting priority srcnat; policy accept; }' 2>/dev/null || true",
            remove,
            $"nft add rule inet gzctf_teamlab prerouting {request.Protocol} dport {request.ListenPort} dnat ip to {request.TargetAddress}:{request.TargetPort} comment \"{comment}\"",
            $"nft add rule inet gzctf_teamlab postrouting ip daddr {request.TargetAddress} {request.Protocol} dport {request.TargetPort} masquerade comment \"{comment}\""
        };
        foreach (var command in commands)
        {
            var result = await runner.RunAsync(command, token);
            if (!result.Success && command != remove)
                throw new InvalidOperationException(result.Output);
        }
    }

    public async Task RemoveAsync(TeamLabServiceForwardRequest request, CancellationToken token)
    {
        Validate(request);
        var result = await runner.RunAsync(RemoveCommand($"gzctf-teamlab-service-{request.AccessId:N}"), token);
        if (!result.Success)
            throw new InvalidOperationException(result.Output);
    }

    private static string RemoveCommand(string comment) =>
        $"for chain in prerouting postrouting; do handles=$(nft -a list chain inet gzctf_teamlab $chain 2>/dev/null | awk '/comment \"{comment}\"/ {{print $NF}}') || continue; " +
        "for handle in $handles; do nft delete rule inet gzctf_teamlab $chain handle $handle || exit 1; done; done";

    private static void Validate(TeamLabServiceForwardRequest request)
    {
        if (request.Protocol is not ("tcp" or "udp") || request.ListenPort is < 1 or > 65535 ||
            request.TargetPort is < 1 or > 65535 || !IPAddress.TryParse(request.TargetAddress, out _))
            throw new ArgumentException("Invalid service forwarding request.");
    }
}
