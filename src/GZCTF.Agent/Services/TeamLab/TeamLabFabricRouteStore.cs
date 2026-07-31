using System.Text.Json;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabFabricRouteStore(IOptions<AgentTeamLabConfig> options)
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path = Path.Combine(
        options.Value.RuntimeStateRoot,
        "fabric",
        "route-declarations.json");

    public async Task<TeamLabFabricRouteState> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(_path)) return TeamLabFabricRouteState.Empty;

        try
        {
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<TeamLabFabricRouteState>(
                stream,
                JsonOptions,
                token);
            if (state is null || state.Version != CurrentVersion)
                throw new InvalidDataException("Unsupported TeamLab Fabric route declaration state.");
            Validate(state);
            return Normalize(state);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid TeamLab Fabric route declaration state.", exception);
        }
    }

    public async Task WriteAsync(TeamLabFabricRouteState state, CancellationToken token)
    {
        var normalized = Normalize(state with { Version = CurrentVersion });
        Validate(normalized);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, token);
                await stream.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static TeamLabFabricRouteState Normalize(TeamLabFabricRouteState state) => state with
    {
        Declarations = state.Declarations
            .OrderBy(item => item.RuntimeId)
            .ThenBy(item => item.Generation)
            .Select(item => item with
            {
                Routes = item.Routes
                    .Distinct()
                    .OrderBy(route => route.TargetCidr, StringComparer.Ordinal)
                    .ThenBy(route => route.GatewayIp, StringComparer.Ordinal)
                    .ToArray()
            })
            .ToArray(),
        ManagedCidrs = state.ManagedCidrs
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray()
    };

    private static void Validate(TeamLabFabricRouteState state)
    {
        if (state.Declarations is null || state.ManagedCidrs is null ||
            state.Declarations.Any(item => item is null || item.Routes is null))
            throw new InvalidDataException("Incomplete TeamLab Fabric route declaration state.");
        if (state.Declarations.GroupBy(item => (item.RuntimeId, item.Generation)).Any(group => group.Count() > 1))
            throw new InvalidDataException("Duplicate TeamLab Fabric route declaration identity.");

        foreach (var declaration in state.Declarations)
        {
            if (declaration.RuntimeId <= 0 || declaration.Generation <= 0 || declaration.RouteVersion <= 0)
                throw new InvalidDataException("Invalid TeamLab Fabric route declaration identity.");
            foreach (var route in declaration.Routes)
            {
                if (TeamLabNetworkPrimitives.ValidateCidr(route.TargetCidr, nameof(route.TargetCidr)) is not null ||
                    TeamLabNetworkPrimitives.ValidateIp(route.GatewayIp, nameof(route.GatewayIp)) is not null)
                    throw new InvalidDataException("Invalid TeamLab Fabric route declaration entry.");
            }
        }

        if (state.ManagedCidrs.Any(cidr =>
                TeamLabNetworkPrimitives.ValidateCidr(cidr, nameof(state.ManagedCidrs)) is not null))
            throw new InvalidDataException("Invalid managed TeamLab Fabric CIDR.");
    }
}

public sealed record TeamLabFabricRouteState(
    int Version,
    TeamLabFabricRouteDeclaration[] Declarations,
    string[] ManagedCidrs)
{
    public static TeamLabFabricRouteState Empty { get; } = new(1, [], []);
}

public sealed record TeamLabFabricRouteDeclaration(
    int RuntimeId,
    int Generation,
    int RouteVersion,
    TeamLabFabricRouteClaim[] Routes,
    DateTimeOffset UpdatedAt);

public sealed record TeamLabFabricRouteClaim(string TargetCidr, string GatewayIp);
