using GZCTF.Models;
using GZCTF.Models.Request.Admin;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Infrastructure.Observability;

/// <summary>Reads a bounded page of display labels; recorded snapshots always take priority.</summary>
public sealed class EfOperationalLogDisplayQuery(AppDbContext context) : IOperationalLogDisplayQuery
{
    public async Task EnrichAsync(IReadOnlyList<LogMessageModel> rows, CancellationToken token)
    {
        if (rows.Count == 0) return;
        var nodeIds = rows.Select(row => row.WorkerNodeId).OfType<Guid>()
            .Concat(ResourceGuids(rows, "worker-node")).Distinct().ToArray();
        var userIds = rows.Where(row => row.ActorDisplayName is null)
            .Select(row => row.ActorUserId).OfType<Guid>().Concat(ResourceGuids(rows, "user")).Distinct().ToArray();
        var ticketIds = ResourceGuids(rows, "deployment-ticket");
        var vmIds = ResourceGuids(rows, "vm").Concat(ResourceGuids(rows, "vm-instance")).Distinct().ToArray();
        var templateIds = ResourceInts(rows, "image-template");
        var gameIds = ResourceInts(rows, "game");
        var courseIds = ResourceInts(rows, "course").Concat(ResourceInts(rows, "training-course")).Distinct().ToArray();
        var teamIds = ResourceInts(rows, "team");
        var gameChallengeIds = ResourceInts(rows, "game-challenge");
        var exerciseChallengeIds = ResourceInts(rows, "exercise-challenge");
        var runtimeIds = ResourceGuids(rows, "teamlab-runtime");
        var nodes = nodeIds.Length == 0 ? [] : await context.WorkerNodes.AsNoTracking()
            .Where(node => nodeIds.Contains(node.Id)).ToDictionaryAsync(node => node.Id, node => node.Name, token);
        var users = userIds.Length == 0 ? [] : await context.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.UserName, token);
        var tickets = ticketIds.Length == 0 ? [] : await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticketIds.Contains(ticket.Id)).ToDictionaryAsync(ticket => ticket.Id,
                ticket => ticket.ResourceDisplayName ?? ticket.SubjectDisplayName, token);
        var vms = vmIds.Length == 0 ? [] : await context.VmInstances.AsNoTracking()
            .Where(vm => vmIds.Contains(vm.Id)).ToDictionaryAsync(vm => vm.Id, vm => vm.VmName, token);
        var templates = templateIds.Length == 0 ? [] : await context.ImageTemplates.AsNoTracking()
            .Where(template => templateIds.Contains(template.Id)).ToDictionaryAsync(template => template.Id,
                template => template.Name, token);
        var games = gameIds.Length == 0 ? [] : await context.Games.AsNoTracking()
            .Where(game => gameIds.Contains(game.Id)).ToDictionaryAsync(game => game.Id, game => game.Title, token);
        var courses = courseIds.Length == 0 ? [] : await context.TrainingCourses.AsNoTracking()
            .Where(course => courseIds.Contains(course.Id)).ToDictionaryAsync(course => course.Id, course => course.Title, token);
        var teams = teamIds.Length == 0 ? [] : await context.Teams.AsNoTracking()
            .Where(team => teamIds.Contains(team.Id)).ToDictionaryAsync(team => team.Id, team => team.Name, token);
        var gameChallenges = gameChallengeIds.Length == 0 ? [] : await context.GameChallenges.AsNoTracking()
            .Where(challenge => gameChallengeIds.Contains(challenge.Id)).ToDictionaryAsync(challenge => challenge.Id,
                challenge => challenge.Title, token);
        var exerciseChallenges = exerciseChallengeIds.Length == 0 ? [] : await context.ExerciseChallenges.AsNoTracking()
            .Where(challenge => exerciseChallengeIds.Contains(challenge.Id)).ToDictionaryAsync(challenge => challenge.Id,
                challenge => challenge.Title, token);
        var runtimes = runtimeIds.Length == 0 ? [] : await context.TeamLabRuntimes.AsNoTracking()
            .Where(runtime => runtimeIds.Contains(runtime.PublicId)).ToDictionaryAsync(runtime => runtime.PublicId,
                runtime => runtime.ExternalReference, token);
        foreach (var row in rows)
        {
            if (row.WorkerNodeName is null && row.WorkerNodeId is { } nodeId &&
                nodes.GetValueOrDefault(nodeId) is { } nodeName)
            {
                row.WorkerNodeName = Clean(nodeName);
                row.CurrentDisplayNames = [.. row.CurrentDisplayNames ?? [], "node"];
            }
            if (row.ActorDisplayName is null && row.ActorUserId is { } userId &&
                users.GetValueOrDefault(userId) is { } userName)
            {
                row.ActorDisplayName = Clean(userName);
                row.CurrentDisplayNames = [.. row.CurrentDisplayNames ?? [], "actor"];
            }
            if (row.ResourceDisplayName is not null) continue;
            if (Guid.TryParse(row.ResourceId, out var resourceId))
                row.ResourceDisplayName = row.ResourceType switch
                {
                    "worker-node" => nodes.GetValueOrDefault(resourceId),
                    "user" => users.GetValueOrDefault(resourceId),
                    "deployment-ticket" => tickets.GetValueOrDefault(resourceId),
                    "vm" or "vm-instance" => vms.GetValueOrDefault(resourceId),
                    "teamlab-runtime" => runtimes.GetValueOrDefault(resourceId),
                    _ => null
                };
            else if (int.TryParse(row.ResourceId, out var intId))
                row.ResourceDisplayName = row.ResourceType switch
                {
                    "image-template" => templates.GetValueOrDefault(intId),
                    "game" => games.GetValueOrDefault(intId),
                    "course" or "training-course" => courses.GetValueOrDefault(intId),
                    "team" => teams.GetValueOrDefault(intId),
                    "game-challenge" => gameChallenges.GetValueOrDefault(intId),
                    "exercise-challenge" => exerciseChallenges.GetValueOrDefault(intId),
                    _ => null
                };
            row.ResourceDisplayName = Clean(row.ResourceDisplayName);
            if (row.ResourceDisplayName is not null)
                row.CurrentDisplayNames = [.. row.CurrentDisplayNames ?? [], "resource"];
        }
    }

    private static string? Clean(string? value) => value is null ? null :
        EfOperationalEventWriter.SanitizeText(value, 256);

    private static Guid[] ResourceGuids(IReadOnlyList<LogMessageModel> rows, string type) => rows
        .Where(row => row.ResourceType == type && row.ResourceDisplayName is null)
        .Select(row => Guid.TryParse(row.ResourceId, out var id) ? id : (Guid?)null)
        .OfType<Guid>().Distinct().ToArray();

    private static int[] ResourceInts(IReadOnlyList<LogMessageModel> rows, string type) => rows
        .Where(row => row.ResourceType == type && row.ResourceDisplayName is null)
        .Select(row => int.TryParse(row.ResourceId, out var id) ? id : (int?)null)
        .OfType<int>().Distinct().ToArray();
}
