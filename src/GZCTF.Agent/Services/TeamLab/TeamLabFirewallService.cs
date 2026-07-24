using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabFirewallService(
    TeamLabCommandExecutor executor,
    IOptions<AgentTeamLabConfig> options)
{
    private readonly int _fabricMss = Math.Clamp(options.Value.FabricMtu - 40, 536, 8960);

    public Task<TeamLabDryRunResponse> ApplyRuntimePoliciesAsync(
        int runtimeId,
        int generation,
        string namespaceName,
        string fabricInterfaceName,
        IReadOnlyCollection<TeamLabForwardPolicyRequest> policies,
        bool dryRun,
        CancellationToken token)
    {
        var validation = Validate(runtimeId, generation, namespaceName, policies);
        if (validation is not null)
            return Task.FromResult(new TeamLabDryRunResponse(false, dryRun, validation, []));
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(fabricInterfaceName, nameof(fabricInterfaceName));
        if (validation is not null)
            return Task.FromResult(new TeamLabDryRunResponse(false, dryRun, validation, []));
        var commands = TeamLabNetworkPrimitives.HasCommand("nft")
            ? BuildNftRuntimePolicyCommands(runtimeId, generation, namespaceName, fabricInterfaceName, _fabricMss, policies)
            : BuildIptablesRuntimePolicyCommands(runtimeId, generation, namespaceName, fabricInterfaceName, _fabricMss, policies);
        return executor.ExecuteAsync(commands, dryRun, token);
    }

    public Task<TeamLabDryRunResponse> ApplyFabricPoliciesAsync(
        int runtimeId,
        int generation,
        string hostInterface,
        IReadOnlyCollection<TeamLabStaticRouteRequest> localRoutes,
        IReadOnlyCollection<TeamLabStaticRouteRequest> remoteRoutes,
        bool dryRun,
        CancellationToken token)
    {
        var validation = ValidateIds(runtimeId, generation);
        if (validation is not null)
            return Task.FromResult(new TeamLabDryRunResponse(false, dryRun, validation, []));
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(hostInterface, nameof(hostInterface));
        if (validation is not null)
            return Task.FromResult(new TeamLabDryRunResponse(false, dryRun, validation, []));
        foreach (var route in localRoutes.Concat(remoteRoutes))
        {
            validation = TeamLabNetworkPrimitives.ValidateCidr(route.TargetCidr, nameof(route.TargetCidr));
            if (validation is not null)
                return Task.FromResult(new TeamLabDryRunResponse(false, dryRun, validation, []));
        }
        var routes = localRoutes.Concat(remoteRoutes)
            .Select(item => item.TargetCidr)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var commands = TeamLabNetworkPrimitives.HasCommand("nft")
            ? BuildNftFabricPolicyCommands(runtimeId, generation, hostInterface, routes)
            : BuildIptablesFabricPolicyCommands(runtimeId, generation, hostInterface, routes);
        return executor.ExecuteAsync(commands, dryRun, token);
    }

    public Task<TeamLabDryRunResponse> RemoveRuntimePoliciesAsync(
        int runtimeId,
        int generation,
        string namespaceName,
        bool dryRun,
        CancellationToken token)
    {
        var validation = ValidateIds(runtimeId, generation) ??
                         TeamLabNetworkPrimitives.ValidateLinuxName(namespaceName, nameof(namespaceName));
        if (validation is not null)
            return Task.FromResult(new TeamLabDryRunResponse(false, dryRun, validation, []));
        var chain = ChainName(runtimeId, generation);
        var mssChain = MssChainName(runtimeId, generation);
        var accessChain = AccessChainName(runtimeId, generation);
        IReadOnlyList<string> commands = TeamLabNetworkPrimitives.HasCommand("nft")
            ? [
                $"ip netns exec {namespaceName} nft flush chain inet gzctf_teamlab {chain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft delete chain inet gzctf_teamlab {chain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft flush chain inet gzctf_teamlab {accessChain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft delete chain inet gzctf_teamlab {accessChain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft flush chain inet gzctf_teamlab {mssChain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft delete chain inet gzctf_teamlab {mssChain} 2>/dev/null || true"
            ]
            : [
                $"while ip netns exec {namespaceName} iptables -C FORWARD -j {chain} 2>/dev/null; do ip netns exec {namespaceName} iptables -D FORWARD -j {chain}; done",
                $"ip netns exec {namespaceName} iptables -F {chain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} iptables -X {chain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} iptables -F {accessChain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} iptables -X {accessChain} 2>/dev/null || true",
                $"while ip netns exec {namespaceName} iptables -t mangle -C FORWARD -j {mssChain} 2>/dev/null; do ip netns exec {namespaceName} iptables -t mangle -D FORWARD -j {mssChain}; done",
                $"ip netns exec {namespaceName} iptables -t mangle -F {mssChain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} iptables -t mangle -X {mssChain} 2>/dev/null || true"
            ];
        return executor.ExecuteAsync(commands, dryRun, token);
    }

    public Task<TeamLabDryRunResponse> RemoveFabricPoliciesAsync(
        int runtimeId,
        int generation,
        bool dryRun,
        CancellationToken token)
    {
        var validation = ValidateIds(runtimeId, generation);
        if (validation is not null)
            return Task.FromResult(new TeamLabDryRunResponse(false, dryRun, validation, []));
        var chain = FabricChainName(runtimeId, generation);
        IReadOnlyList<string> commands = TeamLabNetworkPrimitives.HasCommand("nft")
            ? [
                $"nft flush chain inet gzctf_teamlab {chain} 2>/dev/null || true",
                $"nft delete chain inet gzctf_teamlab {chain} 2>/dev/null || true"
            ]
            : [
                $"while iptables -C FORWARD -j {chain} 2>/dev/null; do iptables -D FORWARD -j {chain}; done",
                $"iptables -F {chain} 2>/dev/null || true",
                $"iptables -X {chain} 2>/dev/null || true"
            ];
        return executor.ExecuteAsync(commands, dryRun, token);
    }

    public Task<TeamLabDryRunResponse> VerifyPoliciesRemovedAsync(
        int runtimeId,
        int generation,
        string namespaceName,
        bool dryRun,
        CancellationToken token)
    {
        var validation = ValidateIds(runtimeId, generation) ??
                         TeamLabNetworkPrimitives.ValidateLinuxName(namespaceName, nameof(namespaceName));
        if (validation is not null)
            return Task.FromResult(new TeamLabDryRunResponse(false, dryRun, validation, []));
        var chain = ChainName(runtimeId, generation);
        var mssChain = MssChainName(runtimeId, generation);
        var accessChain = AccessChainName(runtimeId, generation);
        var fabricChain = FabricChainName(runtimeId, generation);
        IReadOnlyList<string> commands = TeamLabNetworkPrimitives.HasCommand("nft")
            ? [
                $"if ip netns list | awk '{{print $1}}' | grep -Fx {TeamLabNetworkPrimitives.ShellQuote(namespaceName)} >/dev/null; then ! ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {chain} >/dev/null 2>&1 && ! ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {accessChain} >/dev/null 2>&1 && ! ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {mssChain} >/dev/null 2>&1; fi",
                $"! nft list chain inet gzctf_teamlab {fabricChain} >/dev/null 2>&1"
            ]
            : [
                $"if ip netns list | awk '{{print $1}}' | grep -Fx {TeamLabNetworkPrimitives.ShellQuote(namespaceName)} >/dev/null; then ! ip netns exec {namespaceName} iptables -S {chain} >/dev/null 2>&1 && ! ip netns exec {namespaceName} iptables -S {accessChain} >/dev/null 2>&1 && ! ip netns exec {namespaceName} iptables -t mangle -S {mssChain} >/dev/null 2>&1; fi",
                $"! iptables -S {fabricChain} >/dev/null 2>&1"
            ];
        return executor.ExecuteAsync(commands, dryRun, token);
    }

    private static string[] BuildNftRuntimePolicyCommands(
        int runtimeId,
        int generation,
        string namespaceName,
        string fabricInterfaceName,
        int fabricMss,
        IEnumerable<TeamLabForwardPolicyRequest> policies)
    {
        var chain = ChainName(runtimeId, generation);
        var mssChain = MssChainName(runtimeId, generation);
        var accessChain = AccessChainName(runtimeId, generation);
        var prefix = $"ip netns exec {namespaceName} nft";
        var commands = new List<string>
        {
            $"{prefix} add table inet gzctf_teamlab 2>/dev/null || true",
            $"{prefix} {TeamLabNetworkPrimitives.ShellQuote($"add chain inet gzctf_teamlab {mssChain} {{ type filter hook forward priority -10; policy accept; }}")} 2>/dev/null || true",
            $"{prefix} flush chain inet gzctf_teamlab {mssChain}",
            $"{prefix} add rule inet gzctf_teamlab {mssChain} oifname {TeamLabNetworkPrimitives.ShellQuote(fabricInterfaceName)} tcp flags syn tcp option maxseg size set {fabricMss}",
            $"{prefix} add chain inet gzctf_teamlab {accessChain} 2>/dev/null || true",
            $"{prefix} flush chain inet gzctf_teamlab {accessChain}",
            $"{prefix} add rule inet gzctf_teamlab {accessChain} return",
            $"{prefix} {TeamLabNetworkPrimitives.ShellQuote($"add chain inet gzctf_teamlab {chain} {{ type filter hook forward priority 0; policy drop; }}")} 2>/dev/null || true",
            $"{prefix} flush chain inet gzctf_teamlab {chain}",
            $"{prefix} add rule inet gzctf_teamlab {chain} ct state established,related accept",
            $"{prefix} add rule inet gzctf_teamlab {chain} jump {accessChain}"
        };
        commands.AddRange(Ordered(policies).Select(policy =>
            $"{prefix} add rule inet gzctf_teamlab {chain} ip saddr {policy.SourceCidr} ip daddr {policy.DestinationCidr} {(policy.Allow ? "accept" : "reject")}"));
        return commands.ToArray();
    }

    private static string[] BuildIptablesRuntimePolicyCommands(
        int runtimeId,
        int generation,
        string namespaceName,
        string fabricInterfaceName,
        int fabricMss,
        IEnumerable<TeamLabForwardPolicyRequest> policies)
    {
        var chain = ChainName(runtimeId, generation);
        var mssChain = MssChainName(runtimeId, generation);
        var accessChain = AccessChainName(runtimeId, generation);
        var prefix = $"ip netns exec {namespaceName} iptables";
        var commands = new List<string>
        {
            $"{prefix} -t mangle -N {mssChain} 2>/dev/null || true",
            $"{prefix} -t mangle -F {mssChain}",
            $"{prefix} -t mangle -C FORWARD -j {mssChain} 2>/dev/null || {prefix} -t mangle -I FORWARD 1 -j {mssChain}",
            $"{prefix} -t mangle -A {mssChain} -o {fabricInterfaceName} -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --set-mss {fabricMss}",
            $"{prefix} -N {accessChain} 2>/dev/null || true",
            $"{prefix} -F {accessChain}",
            $"{prefix} -A {accessChain} -j RETURN",
            $"{prefix} -N {chain} 2>/dev/null || true",
            $"{prefix} -F {chain}",
            $"{prefix} -C FORWARD -j {chain} 2>/dev/null || {prefix} -I FORWARD 1 -j {chain}",
            $"{prefix} -A {chain} -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT",
            $"{prefix} -A {chain} -j {accessChain}"
        };
        commands.AddRange(Ordered(policies).Select(policy =>
            $"{prefix} -A {chain} -s {policy.SourceCidr} -d {policy.DestinationCidr} -j {(policy.Allow ? "ACCEPT" : "REJECT")}"));
        commands.Add($"{prefix} -A {chain} -j REJECT");
        return commands.ToArray();
    }

    private static string[] BuildNftFabricPolicyCommands(
        int runtimeId,
        int generation,
        string hostInterface,
        IEnumerable<string> routes)
    {
        var chain = FabricChainName(runtimeId, generation);
        var commands = new List<string>
        {
            "nft add table inet gzctf_teamlab 2>/dev/null || true",
            $"nft {TeamLabNetworkPrimitives.ShellQuote($"add chain inet gzctf_teamlab {chain} {{ type filter hook forward priority -50; policy accept; }}")} 2>/dev/null || true",
            $"nft flush chain inet gzctf_teamlab {chain}"
        };
        foreach (var route in routes)
        {
            commands.Add($"nft add rule inet gzctf_teamlab {chain} iifname {TeamLabNetworkPrimitives.ShellQuote(hostInterface)} ip daddr {route} accept");
            commands.Add($"nft add rule inet gzctf_teamlab {chain} oifname {TeamLabNetworkPrimitives.ShellQuote(hostInterface)} ip saddr {route} accept");
        }
        return commands.ToArray();
    }

    private static string[] BuildIptablesFabricPolicyCommands(
        int runtimeId,
        int generation,
        string hostInterface,
        IEnumerable<string> routes)
    {
        var chain = FabricChainName(runtimeId, generation);
        var commands = new List<string>
        {
            $"iptables -N {chain} 2>/dev/null || true",
            $"iptables -F {chain}",
            $"iptables -C FORWARD -j {chain} 2>/dev/null || iptables -I FORWARD 1 -j {chain}"
        };
        foreach (var route in routes)
        {
            commands.Add($"iptables -A {chain} -i {hostInterface} -d {route} -j ACCEPT");
            commands.Add($"iptables -A {chain} -o {hostInterface} -s {route} -j ACCEPT");
        }
        commands.Add($"iptables -A {chain} -j RETURN");
        return commands.ToArray();
    }

    private static IEnumerable<TeamLabForwardPolicyRequest> Ordered(
        IEnumerable<TeamLabForwardPolicyRequest> policies) => policies
        .OrderBy(item => item.Allow ? 0 : 1)
        .ThenBy(item => item.SourceCidr, StringComparer.Ordinal)
        .ThenBy(item => item.DestinationCidr, StringComparer.Ordinal);

    private static string? Validate(
        int runtimeId,
        int generation,
        string namespaceName,
        IEnumerable<TeamLabForwardPolicyRequest> policies)
    {
        var validation = ValidateIds(runtimeId, generation) ??
                         TeamLabNetworkPrimitives.ValidateLinuxName(namespaceName, nameof(namespaceName));
        if (validation is not null) return validation;
        foreach (var policy in policies)
        {
            validation = TeamLabNetworkPrimitives.ValidateCidr(policy.SourceCidr, nameof(policy.SourceCidr));
            if (validation is not null) return validation;
            validation = TeamLabNetworkPrimitives.ValidateCidr(policy.DestinationCidr, nameof(policy.DestinationCidr));
            if (validation is not null) return validation;
        }
        return null;
    }

    private static string? ValidateIds(int runtimeId, int generation) =>
        runtimeId <= 0 ? "Invalid RuntimeId." : generation <= 0 ? "Invalid Generation." : null;

    private static string ChainName(int runtimeId, int generation) => $"TLR{runtimeId:X}G{generation:X}";
    internal static string AccessChainName(int runtimeId, int generation) => $"TLA{runtimeId:X}G{generation:X}";
    private static string MssChainName(int runtimeId, int generation) => $"TLM{runtimeId:X}G{generation:X}";
    private static string FabricChainName(int runtimeId, int generation) => $"TLF{runtimeId:X}G{generation:X}";
}
