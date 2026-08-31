using System.Collections.Concurrent;
using System.Text.Json;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.Observation;

public sealed record ObservationPointRegistration(
    int RuntimeId,
    int Generation,
    Guid PublicId,
    string TopologyKey,
    byte Kind,
    string InterfaceName);

public sealed class ObservationPointRegistry(ILogger<ObservationPointRegistry> logger)
{
    private const string RegistryFileName = "observation-points.json";
    private readonly ConcurrentDictionary<string, ObservationPointRegistration> _registrations =
        new(StringComparer.Ordinal);

    public IReadOnlyList<ObservationPointRegistration> Snapshot() =>
        _registrations.Values
            .OrderBy(item => item.RuntimeId)
            .ThenBy(item => item.Generation)
            .ThenBy(item => item.PublicId)
            .ThenBy(item => item.InterfaceName, StringComparer.Ordinal)
            .ToArray();

    public async Task ApplyAsync(
        TeamLabInfrastructureApplyRequest request,
        CancellationToken cancellationToken)
    {
        var registrations = Resolve(request);
        await PersistGenerationAsync(request.RuntimeId, request.Generation, registrations, cancellationToken);
    }

    public async Task ApplyExecutionPlanAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        var registrations = plan.ObservationPoints
            .Where(point => point.ObservationPointId != Guid.Empty &&
                            !string.IsNullOrWhiteSpace(point.InterfaceToken))
            .Select(point => new ObservationPointRegistration(
                plan.RuntimeId,
                plan.Generation,
                point.ObservationPointId,
                point.AssetKey,
                3,
                point.InterfaceToken))
            .DistinctBy(item => (item.PublicId, item.InterfaceName))
            .ToArray();
        await PersistGenerationAsync(plan.RuntimeId, plan.Generation, registrations, cancellationToken);
    }

    async Task PersistGenerationAsync(
        int runtimeId,
        int generation,
        IReadOnlyCollection<ObservationPointRegistration> registrations,
        CancellationToken cancellationToken)
    {
        ReplaceGeneration(runtimeId, generation, registrations);
        var path = RegistryPath(runtimeId, generation);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, registrations, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task RemoveAsync(int runtimeId, int generation)
    {
        ReplaceGeneration(runtimeId, generation, []);
        var path = RegistryPath(runtimeId, generation);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        const string root = "/run/gzctf-teamlab";
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, RegistryFileName, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var registrations = await JsonSerializer.DeserializeAsync<ObservationPointRegistration[]>(
                    stream, cancellationToken: cancellationToken) ?? [];
                foreach (var generation in registrations.GroupBy(item => (item.RuntimeId, item.Generation)))
                    ReplaceGeneration(generation.Key.RuntimeId, generation.Key.Generation, generation.ToArray());
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Failed to restore TeamLab observation registry from {Path}.", path);
            }
        }
    }

    internal static ObservationPointRegistration[] Resolve(TeamLabInfrastructureApplyRequest request)
    {
        var switchIndexes = request.Switches
            .Select((item, index) => (item.Key, item.BridgeName, Index: index))
            .ToDictionary(item => item.Key, StringComparer.Ordinal);
        var routers = request.Routers.ToDictionary(item => item.Key, StringComparer.Ordinal);
        List<ObservationPointRegistration> registrations = [];
        foreach (var point in request.ObservationPoints.OrderBy(item => item.PublicId))
        {
            IEnumerable<string> interfaces = point.Kind switch
            {
                0 => [request.Switches.Single(item => item.Key == point.TopologyKey).BridgeName],
                1 => routers[point.TopologyKey].NetworkKeys
                    .Select(key => switchIndexes[key].Index)
                    .Order()
                    .Select(index => TeamLabNetworkPrimitives.TrimInterfaceName($"{request.RouterNamespace}h{index}")),
                2 => [request.Fabric.HostInterfaceName],
                3 => [point.InterfaceToken],
                _ => throw new InvalidOperationException("Unknown TeamLab observation point kind.")
            };
            registrations.AddRange(interfaces.Distinct(StringComparer.Ordinal).Select(interfaceName =>
                new ObservationPointRegistration(
                    request.RuntimeId,
                    request.Generation,
                    point.PublicId,
                    point.TopologyKey,
                    point.Kind,
                    interfaceName)));
        }

        return registrations
            .OrderBy(item => item.PublicId)
            .ThenBy(item => item.InterfaceName, StringComparer.Ordinal)
            .ToArray();
    }

    private void ReplaceGeneration(
        int runtimeId,
        int generation,
        IReadOnlyCollection<ObservationPointRegistration> registrations)
    {
        foreach (var key in _registrations
                     .Where(item => item.Value.RuntimeId == runtimeId && item.Value.Generation == generation)
                     .Select(item => item.Key)
                     .ToArray())
            _registrations.TryRemove(key, out _);
        foreach (var registration in registrations)
            _registrations[Key(registration)] = registration;
    }

    private static string Key(ObservationPointRegistration registration) =>
        $"{registration.RuntimeId}:{registration.Generation}:{registration.PublicId:N}:{registration.InterfaceName}";

    private static string RegistryPath(int runtimeId, int generation) =>
        $"{TeamLabNetworkService.ResolveDesiredStateDirectory(runtimeId, generation)}/{RegistryFileName}";
}
