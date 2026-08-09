using System.Text.Json;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabRuntimeGenerationStore(IOptions<AgentTeamLabConfig> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _root = options.Value.RuntimeStateRoot;

    public async Task<TeamLabActiveGeneration?> ReadAsync(int runtimeId, CancellationToken token)
    {
        var path = ResolvePath(runtimeId);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<TeamLabActiveGeneration>(stream,
                JsonOptions,
                cancellationToken: token);
            if (state is null || state.RuntimeId != runtimeId || state.Generation <= 0)
                throw new InvalidDataException($"Invalid active generation state for runtime {runtimeId}.");
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid active generation state for runtime {runtimeId}.", exception);
        }
    }

    public async Task WriteAsync(int runtimeId, int generation, CancellationToken token)
    {
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        var path = ResolvePath(runtimeId);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream,
                    new TeamLabActiveGeneration(runtimeId, generation, DateTimeOffset.UtcNow),
                    JsonOptions,
                    cancellationToken: token);
                await stream.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<bool> ClearIfActiveAsync(int runtimeId, int generation, CancellationToken token)
    {
        var current = await ReadAsync(runtimeId, token);
        if (current?.Generation != generation) return false;
        File.Delete(ResolvePath(runtimeId));
        return true;
    }

    internal string ResolvePath(int runtimeId)
    {
        if (runtimeId <= 0) throw new ArgumentOutOfRangeException(nameof(runtimeId));
        return Path.Combine(_root, $"runtime-{runtimeId}", "active-generation.json");
    }
}

public sealed record TeamLabActiveGeneration(
    int RuntimeId,
    int Generation,
    DateTimeOffset ActivatedAt);
