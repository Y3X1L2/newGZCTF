using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Application;

public sealed class PenetrationTeamLabAdapter(
    AppDbContext context,
    ITeamLabRuntimeApplicationService runtimes,
    PenetrationObjectiveService objectives)
{
    public async Task<PenetrationGameLabBindingModel?> GetBindingAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationGameLabBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken);
        if (binding is null) return null;
        var topologyId = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.Id == binding.TopologyId)
            .Select(item => item.PublicId)
            .SingleAsync(cancellationToken);
        return new PenetrationGameLabBindingModel(
            gameId, topologyId, binding.ActiveReleaseId, binding.MaxResetCount,
            await objectives.ListAsync(gameId, cancellationToken));
    }

    public async Task<PenetrationGameLabBindingModel> BindAsync(
        int gameId,
        Guid topologyPublicId,
        CancellationToken cancellationToken)
    {
        var topologyId = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.PublicId == topologyPublicId)
            .Select(item => (int?)item.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The TeamLab topology was not found.");
        var binding = await context.PenetrationGameLabBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken);
        if (binding is null)
        {
            binding = new PenetrationGameLabBinding { GameId = gameId, TopologyId = topologyId };
            context.PenetrationGameLabBindings.Add(binding);
        }
        else if (binding.TopologyId != topologyId)
        {
            var hasRuntime = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
                .AnyAsync(item => item.GameId == gameId, cancellationToken);
            if (hasRuntime) throw new InvalidOperationException("Destroy existing team runtimes before rebinding the topology.");
            binding.TopologyId = topologyId;
            binding.ActiveReleaseId = null;
        }
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return (await GetBindingAsync(gameId, cancellationToken))!;
    }

    public async Task<PenetrationGameLabBindingModel> ActivateReleaseAsync(
        int gameId,
        Guid releasePublicId,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationGameLabBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game has no TeamLab topology binding.");
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releasePublicId && item.TopologyId == binding.TopologyId,
                cancellationToken)
            ?? throw new InvalidOperationException("The release does not belong to the bound topology.");
        binding.ActiveReleaseId = release.Id;
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return (await GetBindingAsync(gameId, cancellationToken))!;
    }

    public async Task<(int Created, int Reused)> DeployGameAsync(
        int gameId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var teamIds = await context.Participations.AsNoTracking()
            .Where(item => item.GameId == gameId && item.Status == ParticipationStatus.Accepted)
            .Select(item => item.TeamId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var results = await Task.WhenAll(teamIds.Select(teamId => DeployTeamAsync(
            gameId, teamId, actorUserId, cancellationToken)));
        return (results.Count(item => !item.Reused), results.Count(item => item.Reused));
    }

    public async Task<TeamLabRuntimeCreateResult> DeployTeamAsync(
        int gameId,
        int teamId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationGameLabBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game has no TeamLab topology binding.");
        if (binding.ActiveReleaseId is not { } releaseId)
            throw new InvalidOperationException("Publish and activate a TeamLab topology release before deployment.");
        var runtimeOwnerUserId = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.Id == binding.TopologyId)
            .Select(item => item.OwnerUserId)
            .SingleAsync(cancellationToken)
            ?? throw new InvalidOperationException("The bound TeamLab topology has no owner.");
        var overlays = await objectives.BuildOverlaysAsync(gameId, teamId, releaseId, cancellationToken);
        var command = new CreateTeamLabRuntimeModel(
            releaseId, $"penetration:{gameId}:team:{teamId}", null, overlays);
        var result = await runtimes.PlanAndEnqueueAsync(
            command, actorUserId, runtimeOwnerUserId, Hash(command), null,
            $"game {gameId} / team {teamId}", cancellationToken);
        var runtimeBinding = await context.PenetrationTeamRuntimeBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.TeamId == teamId, cancellationToken);
        if (runtimeBinding is null)
        {
            context.PenetrationTeamRuntimeBindings.Add(new PenetrationTeamRuntimeBinding
            {
                GameId = gameId,
                TeamId = teamId,
                RuntimeId = result.RuntimeId
            });
        }
        else
        {
            runtimeBinding.RuntimeId = result.RuntimeId;
        }
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<TeamLabRuntimeCreateResult> ResetTeamAsync(
        int gameId,
        int teamId,
        Guid userId,
        bool byAdmin,
        CancellationToken cancellationToken)
    {
        var binding = await LoadRuntimeBindingAsync(gameId, teamId, cancellationToken);
        var settings = await context.PenetrationGameLabBindings.AsNoTracking()
            .SingleAsync(item => item.GameId == gameId, cancellationToken);
        var resetCount = await context.PenetrationResetRecords.AsNoTracking()
            .CountAsync(item => item.RuntimeId == binding.RuntimeId, cancellationToken);
        if (!byAdmin && resetCount >= settings.MaxResetCount)
            throw new InvalidOperationException("Reset limit reached.");
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleAsync(item => item.Id == binding.RuntimeId, cancellationToken);
        if (settings.ActiveReleaseId is not { } releaseId)
            throw new InvalidOperationException("The game has no active TeamLab release.");
        var overlays = await objectives.BuildOverlaysAsync(gameId, teamId, releaseId, cancellationToken);
        var result = await runtimes.ResetAndEnqueueAsync(
            runtime.PublicId, new ResetTeamLabRuntimeModel(overlays, releaseId), null, cancellationToken);
        context.PenetrationResetRecords.Add(new PenetrationResetRecord
        {
            RuntimeId = binding.RuntimeId,
            UserId = userId,
            ByAdmin = byAdmin,
            ResetAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task DestroyTeamAsync(int gameId, int teamId, CancellationToken cancellationToken)
    {
        var binding = await LoadRuntimeBindingAsync(gameId, teamId, cancellationToken);
        var runtimeId = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.Id == binding.RuntimeId)
            .Select(item => item.PublicId)
            .SingleAsync(cancellationToken);
        await runtimes.DestroyAsync(runtimeId, cancellationToken);
        context.PenetrationTeamRuntimeBindings.Remove(binding);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DestroyGameAsync(int gameId, CancellationToken cancellationToken)
    {
        var teams = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => item.TeamId)
            .ToArrayAsync(cancellationToken);
        foreach (var teamId in teams) await DestroyTeamAsync(gameId, teamId, cancellationToken);
    }

    public async Task<IReadOnlyList<PenetrationRuntimeBindingModel>> ListRuntimesAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        var rows = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .Select(item => new
            {
                item.TeamId,
                TeamName = context.Teams.Where(team => team.Id == item.TeamId).Select(team => team.Name).First(),
                Runtime = context.TeamLabRuntimes.Where(runtime => runtime.Id == item.RuntimeId).Select(runtime => new
                {
                    runtime.PublicId, runtime.Generation, runtime.Status,
                    ShardCount = runtime.Shards.Count(shard => shard.Generation == runtime.Generation),
                    AssetCount = runtime.Assets.Count(asset => asset.Generation == runtime.Generation),
                    runtime.CreatedAt, runtime.UpdatedAt, runtime.LastError
                }).First()
            }).OrderBy(item => item.TeamName)
            .ToArrayAsync(cancellationToken);
        return rows.Select(item => new PenetrationRuntimeBindingModel(
            item.TeamId, item.TeamName, item.Runtime.PublicId, item.Runtime.Generation, item.Runtime.Status,
            item.Runtime.Status.ToString().ToLowerInvariant(), item.Runtime.ShardCount, item.Runtime.AssetCount, item.Runtime.CreatedAt,
            item.Runtime.UpdatedAt, item.Runtime.LastError)).ToArray();
    }

    private async Task<PenetrationTeamRuntimeBinding> LoadRuntimeBindingAsync(
        int gameId,
        int teamId,
        CancellationToken cancellationToken) =>
        await context.PenetrationTeamRuntimeBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.TeamId == teamId, cancellationToken)
        ?? throw new InvalidOperationException("The team has no TeamLab runtime binding.");

    private static string Hash(CreateTeamLabRuntimeModel command)
    {
        var json = JsonSerializer.Serialize(command);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
