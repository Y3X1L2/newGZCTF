using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabFabricService(
    TeamLabCommandExecutor executor,
    TeamLabFirewallService firewall,
    TeamLabCommandRunner runner,
    TeamLabFabricRouteStore routeStore,
    IOptions<AgentTeamLabConfig> options,
    ILogger<TeamLabFabricService> logger) : IHostedService
{
    private readonly AgentTeamLabConfig _config = options.Value;
    private readonly SemaphoreSlim _peerRouteLock = new(1, 1);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enable || _config.DryRun) return;
        try
        {
            var result = await ReconcilePeerRoutesAsync(cancellationToken);
            if (!result.Success)
                logger.LogWarning("TeamLab Fabric route startup reconciliation failed: {Message}", result.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "TeamLab Fabric route startup reconciliation failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<TeamLabDryRunResponse> ApplyAsync(
        TeamLabFabricApplyRequest request,
        CancellationToken token)
    {
        var validation = Validate(request);
        if (validation is not null)
            return new TeamLabDryRunResponse(false, request.DryRun, validation, []);
        var fabricInterface = _config.FabricInterfaceName.Trim();
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(
            fabricInterface, nameof(_config.FabricInterfaceName));
        if (validation is not null)
            return new TeamLabDryRunResponse(false, request.DryRun, validation, []);

        var localRoutes = NormalizeRoutes(request.LocalRoutes ?? []);
        var remoteRoutes = NormalizeRoutes(request.Routes ?? []);
        var namespaceName = request.NamespaceName!;
        var hostInterface = request.HostInterfaceName;
        var namespaceInterface = request.NamespaceInterfaceName;
        var hostAddress = TeamLabNetworkPrimitives.AddressFromCidr(request.NamespaceHostAddressCidr);
        var commands = new List<string>
        {
            TeamLabNetworkPrimitives.BuildEnsureVethPairCommand(
                namespaceName, hostInterface, namespaceInterface),
            $"ip link set {hostInterface} alias {TeamLabNetworkPrimitives.ShellQuote($"gzctf-teamlab-fabric:{request.RuntimeId}")}",
            TeamLabNetworkPrimitives.BuildHostIpv4AddressConvergenceCommand(
                hostInterface, request.NamespaceHostAddressCidr),
            $"ip link set {hostInterface} up",
            TeamLabNetworkPrimitives.BuildNamespaceIpv4AddressConvergenceCommand(
                namespaceName, namespaceInterface, request.NamespacePeerAddressCidr),
            $"ip netns exec {namespaceName} ip link set {namespaceInterface} up",
            "sysctl -w net.ipv4.ip_forward=1",
            $"ip netns exec {namespaceName} sysctl -w net.ipv4.ip_forward=1"
        };
        commands.AddRange(localRoutes.Select(route =>
            $"ip route replace {route.TargetCidr} via {route.GatewayIp} dev {hostInterface}"));
        commands.AddRange(remoteRoutes.Select(route =>
            $"ip netns exec {namespaceName} ip route replace {route.TargetCidr} via {hostAddress} dev {namespaceInterface}{SourceClause(route)}"));
        commands.AddRange(remoteRoutes.Select(route =>
            $"ip route replace {route.TargetCidr} via {route.GatewayIp} dev {fabricInterface}"));

        var network = await executor.ExecuteAsync(commands, request.DryRun, token);
        if (!network.Success) return network;
        var runtimePolicy = await firewall.ApplyRuntimePoliciesAsync(
            request.RuntimeId,
            request.Generation,
            namespaceName,
            namespaceInterface,
            request.ForwardPolicies ?? [],
            request.DryRun,
            token);
        if (!runtimePolicy.Success) return Merge(network, runtimePolicy);
        var fabricPolicy = await firewall.ApplyFabricPoliciesAsync(
            request.RuntimeId,
            request.Generation,
            hostInterface,
            localRoutes,
            remoteRoutes,
            request.DryRun,
            token);
        if (!fabricPolicy.Success) return Merge(network, runtimePolicy, fabricPolicy);
        var peerRoutes = await EnsurePeerRoutesAsync(request, token);
        return Merge(network, runtimePolicy, fabricPolicy, peerRoutes);
    }

    public async Task<TeamLabDryRunResponse> EnsurePeerRoutesAsync(
        TeamLabFabricApplyRequest request,
        CancellationToken token)
    {
        var routes = NormalizeRoutes(request.Routes ?? []);
        if (request.RuntimeId <= 0 || request.Generation <= 0 || request.RouteVersion <= 0)
            return new TeamLabDryRunResponse(false, request.DryRun,
                "Invalid TeamLab Fabric route declaration identity.", []);
        foreach (var route in routes)
        {
            var validation = ValidateRoute(route);
            if (validation is not null)
                return new TeamLabDryRunResponse(false, request.DryRun, validation, []);
        }
        if (!_config.Enable || _config.DryRun || request.DryRun)
            return new TeamLabDryRunResponse(true, true,
                "WireGuard peer route reconciliation returned a command plan without execution.",
                routes.Length == 0
                    ? [$"# clear Fabric route declaration for runtime {request.RuntimeId}, generation {request.Generation}"]
                    : routes.Select(route =>
                        $"# declare {route.TargetCidr} through Fabric gateway {route.GatewayIp} for runtime {request.RuntimeId}, generation {request.Generation}").ToArray());

        await _peerRouteLock.WaitAsync(token);
        try
        {
            TeamLabFabricRouteState current;
            try
            {
                current = await routeStore.ReadAsync(token);
            }
            catch (InvalidDataException exception)
            {
                return new TeamLabDryRunResponse(false, false, exception.Message, []);
            }

            var declaration = new TeamLabFabricRouteDeclaration(
                request.RuntimeId,
                request.Generation,
                request.RouteVersion,
                routes.Select(route => new TeamLabFabricRouteClaim(route.TargetCidr, route.GatewayIp)).ToArray(),
                DateTimeOffset.UtcNow);
            var next = ReplaceRuntimeDeclaration(current, declaration);
            var conflict = ValidateClaimConflicts(next);
            if (conflict is not null)
                return new TeamLabDryRunResponse(false, false, conflict, []);

            await routeStore.WriteAsync(next, token);
            var peerRoutes = await ReconcileStateAsync(next, token);
            if (!peerRoutes.Success) return peerRoutes;
            var hostRoutes = await RemoveUnclaimedHostRoutesAsync(current, next, [], token);
            return Merge(peerRoutes, hostRoutes);
        }
        finally
        {
            _peerRouteLock.Release();
        }
    }

    public async Task<TeamLabDryRunResponse> RemovePeerRoutesAsync(
        int runtimeId,
        int generation,
        IReadOnlyCollection<string> remoteCidrs,
        bool dryRun,
        CancellationToken token)
    {
        if (runtimeId <= 0 || generation <= 0)
            return new TeamLabDryRunResponse(false, dryRun,
                "Invalid TeamLab Fabric route declaration identity.", []);
        var cidrs = remoteCidrs.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        foreach (var cidr in cidrs)
        {
            var validation = TeamLabNetworkPrimitives.ValidateCidr(cidr, nameof(remoteCidrs));
            if (validation is not null) return new TeamLabDryRunResponse(false, dryRun, validation, []);
        }
        if (!_config.Enable || _config.DryRun || dryRun)
            return new TeamLabDryRunResponse(true, true,
                "WireGuard peer route cleanup returned a command plan without execution.",
                [$"# remove Fabric route declaration for runtime {runtimeId}, generation {generation}"]);

        await _peerRouteLock.WaitAsync(token);
        try
        {
            TeamLabFabricRouteState current;
            try
            {
                current = await routeStore.ReadAsync(token);
            }
            catch (InvalidDataException exception)
            {
                return new TeamLabDryRunResponse(false, false, exception.Message, []);
            }

            var removed = current.Declarations
                .Where(item => item.RuntimeId == runtimeId && item.Generation == generation)
                .SelectMany(item => item.Routes)
                .Select(item => item.TargetCidr);
            var next = current with
            {
                Declarations = current.Declarations
                    .Where(item => item.RuntimeId != runtimeId || item.Generation != generation)
                    .ToArray(),
                ManagedCidrs = current.ManagedCidrs
                    .Concat(cidrs)
                    .Concat(removed)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray()
            };
            await routeStore.WriteAsync(next, token);
            var peerRoutes = await ReconcileStateAsync(next, token);
            if (!peerRoutes.Success) return peerRoutes;
            var hostRoutes = await RemoveUnclaimedHostRoutesAsync(current, next, cidrs, token);
            return Merge(peerRoutes, hostRoutes);
        }
        finally
        {
            _peerRouteLock.Release();
        }
    }

    public async Task<TeamLabDryRunResponse> ReconcilePeerRoutesAsync(CancellationToken token)
    {
        if (!_config.Enable || _config.DryRun)
            return new TeamLabDryRunResponse(true, true,
                "WireGuard peer route reconciliation is disabled or in dry-run mode.", []);

        await _peerRouteLock.WaitAsync(token);
        try
        {
            TeamLabFabricRouteState state;
            try
            {
                state = await routeStore.ReadAsync(token);
            }
            catch (InvalidDataException exception)
            {
                return new TeamLabDryRunResponse(false, false, exception.Message, []);
            }
            return await ReconcileStateAsync(state, token);
        }
        finally
        {
            _peerRouteLock.Release();
        }
    }

    private async Task<TeamLabDryRunResponse> ReconcileStateAsync(
        TeamLabFabricRouteState state,
        CancellationToken token)
    {
        if (state.Declarations.Length == 0 && state.ManagedCidrs.Length == 0)
            return new TeamLabDryRunResponse(true, false, "No managed WireGuard Fabric routes exist.", []);

        var peers = await ReadPeersAsync(token);
        if (!peers.Success)
            return new TeamLabDryRunResponse(false, false, peers.Error!, []);
        var desired = BuildDesiredAllowedIps(state, peers.Peers!);
        if (!desired.Success)
            return new TeamLabDryRunResponse(false, false, desired.Error!, []);

        var commands = new List<string>();
        foreach (var peer in peers.Peers!)
        {
            var allowedIps = desired.AllowedIps![peer.PublicKey];
            if (peer.AllowedIps.ToHashSet(StringComparer.Ordinal).SetEquals(allowedIps)) continue;
            var command = BuildSetAllowedIpsCommand(peer.PublicKey, allowedIps);
            var result = await runner.RunAsync(command, token);
            commands.Add(command);
            if (!result.Success)
                return new TeamLabDryRunResponse(false, false, result.Output, commands.ToArray());
        }

        var verifiedPeers = await ReadPeersAsync(token);
        if (!verifiedPeers.Success)
            return new TeamLabDryRunResponse(false, false, verifiedPeers.Error!, commands.ToArray());
        if (verifiedPeers.Peers!.Count != desired.AllowedIps!.Count)
            return new TeamLabDryRunResponse(false, false,
                "WireGuard Fabric peer set changed during AllowedIPs reconciliation.",
                commands.ToArray());
        foreach (var peer in verifiedPeers.Peers!)
        {
            if (!desired.AllowedIps.TryGetValue(peer.PublicKey, out var expected) ||
                !peer.AllowedIps.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
                return new TeamLabDryRunResponse(false, false,
                    $"WireGuard Fabric AllowedIPs postcondition failed for peer {peer.PublicKey}.",
                    commands.ToArray());
        }
        return new TeamLabDryRunResponse(true, false,
            commands.Count == 0
                ? "WireGuard Fabric peer routes already match authoritative state."
                : "WireGuard Fabric peer routes reconciled to authoritative state.",
            commands.ToArray());
    }

    private async Task<TeamLabDryRunResponse> RemoveUnclaimedHostRoutesAsync(
        TeamLabFabricRouteState previous,
        TeamLabFabricRouteState current,
        IReadOnlyCollection<string> explicitCidrs,
        CancellationToken token)
    {
        var claimed = current.Declarations.SelectMany(item => item.Routes)
            .Select(item => item.TargetCidr)
            .ToHashSet(StringComparer.Ordinal);
        var dropped = previous.Declarations.SelectMany(item => item.Routes)
            .Select(item => item.TargetCidr)
            .Concat(explicitCidrs)
            .Where(cidr => !claimed.Contains(cidr))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var commands = new List<string>();
        foreach (var cidr in dropped)
        {
            var command = $"if ip route show exact {TeamLabNetworkPrimitives.ShellQuote(cidr)} | grep -q .; then ip route del {TeamLabNetworkPrimitives.ShellQuote(cidr)}; fi";
            var result = await runner.RunAsync(command, token);
            commands.Add(command);
            if (!result.Success)
                return new TeamLabDryRunResponse(false, false, result.Output, commands.ToArray());
        }
        return new TeamLabDryRunResponse(true, false,
            dropped.Length == 0 ? "No unclaimed Fabric host routes require cleanup." : "Unclaimed Fabric host routes removed.",
            commands.ToArray());
    }

    private async Task<(bool Success, IReadOnlyList<FabricPeer>? Peers, string? Error)> ReadPeersAsync(
        CancellationToken token)
    {
        var interfaceName = _config.FabricInterfaceName.Trim();
        var validation = TeamLabNetworkPrimitives.ValidateLinuxName(interfaceName, nameof(_config.FabricInterfaceName));
        if (validation is not null) return (false, null, validation);
        var result = await runner.RunAsync(
            $"wg show {TeamLabNetworkPrimitives.ShellQuote(interfaceName)} allowed-ips", token);
        if (!result.Success) return (false, null, result.Output);
        var peers = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParsePeer)
            .Where(item => item is not null)
            .Cast<FabricPeer>()
            .ToArray();
        return peers.Length == 0
            ? (false, null, "The WireGuard Fabric interface has no peers.")
            : (true, peers, null);
    }

    private string BuildSetAllowedIpsCommand(string publicKey, IReadOnlyCollection<string> allowedIps) =>
        $"wg set {TeamLabNetworkPrimitives.ShellQuote(_config.FabricInterfaceName.Trim())} " +
        $"peer {TeamLabNetworkPrimitives.ShellQuote(publicKey)} allowed-ips " +
        TeamLabNetworkPrimitives.ShellQuote(string.Join(',', allowedIps));

    private static FabricPeer? ParsePeer(string line)
    {
        var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < 2) return null;
        return new FabricPeer(
            fields[0],
            fields[1] == "(none)"
                ? []
                : fields.Skip(1)
                    .SelectMany(field => field.Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToArray());
    }

    private static TeamLabFabricRouteState ReplaceRuntimeDeclaration(
        TeamLabFabricRouteState state,
        TeamLabFabricRouteDeclaration declaration) => state with
    {
        Declarations = state.Declarations
            .Where(item => item.RuntimeId != declaration.RuntimeId)
            .Append(declaration)
            .OrderBy(item => item.RuntimeId)
            .ThenBy(item => item.Generation)
            .ToArray(),
        ManagedCidrs = state.ManagedCidrs
            .Concat(declaration.Routes.Select(item => item.TargetCidr))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray()
    };

    private static string? ValidateClaimConflicts(TeamLabFabricRouteState state)
    {
        foreach (var route in state.Declarations.SelectMany(item => item.Routes))
        {
            if (string.Equals(route.TargetCidr, $"{route.GatewayIp}/32", StringComparison.Ordinal))
                return $"Fabric route {route.TargetCidr} cannot replace its peer ownership address.";
        }
        var conflict = state.Declarations
            .SelectMany(item => item.Routes)
            .GroupBy(item => item.TargetCidr, StringComparer.Ordinal)
            .Select(group => new
            {
                Target = group.Key,
                Gateways = group.Select(item => item.GatewayIp).Distinct(StringComparer.Ordinal).ToArray()
            })
            .FirstOrDefault(item => item.Gateways.Length > 1);
        return conflict is null
            ? null
            : $"Fabric route {conflict.Target} is declared through multiple gateways: {string.Join(", ", conflict.Gateways)}.";
    }

    private static (bool Success, IReadOnlyDictionary<string, string[]>? AllowedIps, string? Error)
        BuildDesiredAllowedIps(TeamLabFabricRouteState state, IReadOnlyList<FabricPeer> peers)
    {
        var conflict = ValidateClaimConflicts(state);
        if (conflict is not null) return (false, null, conflict);

        var managedCidrs = state.ManagedCidrs.ToHashSet(StringComparer.Ordinal);
        var desired = peers.ToDictionary(
            peer => peer.PublicKey,
            peer => peer.AllowedIps.Where(cidr => !managedCidrs.Contains(cidr)).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var route in state.Declarations.SelectMany(item => item.Routes)
                     .Distinct()
                     .OrderBy(item => item.TargetCidr, StringComparer.Ordinal))
        {
            var gatewayCidr = $"{route.GatewayIp}/32";
            var owners = peers
                .Where(peer => peer.AllowedIps.Contains(gatewayCidr, StringComparer.Ordinal))
                .ToArray();
            if (owners.Length != 1)
                return (false, null, owners.Length == 0
                    ? $"No WireGuard Fabric peer owns gateway {gatewayCidr}."
                    : $"Multiple WireGuard Fabric peers own gateway {gatewayCidr}.");
            desired[owners[0].PublicKey].Add(route.TargetCidr);
        }

        return (true, desired.ToDictionary(
            item => item.Key,
            item => item.Value.OrderBy(cidr => cidr, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal), null);
    }

    private static TeamLabDryRunResponse Merge(params TeamLabDryRunResponse[] responses)
    {
        var failure = responses.FirstOrDefault(item => !item.Success);
        return new TeamLabDryRunResponse(
            failure is null,
            responses.Any(item => item.DryRun),
            failure?.Message ?? "Fabric desired state applied.",
            responses.SelectMany(item => item.Commands).ToArray());
    }

    private static string? Validate(TeamLabFabricApplyRequest request)
    {
        if (request.RuntimeId <= 0) return "Invalid RuntimeId.";
        if (request.Generation <= 0) return "Invalid Generation.";
        if (request.RouteVersion <= 0) return "Invalid RouteVersion.";
        var validation = TeamLabNetworkPrimitives.ValidateIp(request.FabricIp, nameof(request.FabricIp));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.NamespaceName ?? string.Empty,
            nameof(request.NamespaceName));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateCidr(request.NamespaceHostAddressCidr,
            nameof(request.NamespaceHostAddressCidr));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateCidr(request.NamespacePeerAddressCidr,
            nameof(request.NamespacePeerAddressCidr));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.HostInterfaceName,
            nameof(request.HostInterfaceName));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.NamespaceInterfaceName,
            nameof(request.NamespaceInterfaceName));
        if (validation is not null) return validation;
        foreach (var route in (request.LocalRoutes ?? []).Concat(request.Routes ?? []))
        {
            validation = ValidateRoute(route);
            if (validation is not null) return validation;
        }
        return null;
    }

    private static string? ValidateRoute(TeamLabStaticRouteRequest route)
    {
        var validation = TeamLabNetworkPrimitives.ValidateCidr(route.TargetCidr, nameof(route.TargetCidr));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateIp(route.GatewayIp, nameof(route.GatewayIp));
        if (validation is not null) return validation;
        return string.IsNullOrWhiteSpace(route.SourceIp)
            ? null
            : TeamLabNetworkPrimitives.ValidateIp(route.SourceIp, nameof(route.SourceIp));
    }

    private static TeamLabStaticRouteRequest[] NormalizeRoutes(IEnumerable<TeamLabStaticRouteRequest> routes) => routes
        .GroupBy(route => route.TargetCidr, StringComparer.Ordinal)
        .Select(group => group.OrderBy(route => route.GatewayIp, StringComparer.Ordinal).First())
        .OrderBy(route => route.TargetCidr, StringComparer.Ordinal)
        .ToArray();

    private static string SourceClause(TeamLabStaticRouteRequest route) =>
        string.IsNullOrWhiteSpace(route.SourceIp) ? string.Empty : $" src {route.SourceIp}";

    private sealed record FabricPeer(string PublicKey, IReadOnlyList<string> AllowedIps);
}
