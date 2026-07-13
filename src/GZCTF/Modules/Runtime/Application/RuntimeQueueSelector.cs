using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Runtime.Application;

public sealed class RuntimeQueueSelector(AppDbContext context, IOptions<RuntimeSchedulingOptions> options)
{
    readonly RuntimeSchedulingOptions _options = options.Value;

    public async Task<Guid[]> SelectAsync(DateTimeOffset now, CancellationToken token)
    {
        var window = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.Status == DeploymentQueueTicketStatus.Pending &&
                             (ticket.NotBeforeAt == null || ticket.NotBeforeAt <= now))
            .OrderBy(ticket => ticket.Operation == RuntimeOperationKind.Create ? 1 : 0)
            .ThenBy(ticket => ticket.CreatedAt)
            .ThenBy(ticket => ticket.Id)
            .Take(Math.Max(_options.SchedulingBatchSize, _options.EligibleWindowSize))
            .ToArrayAsync(token);
        if (window.Length == 0)
            return [];

        var activeSubjects = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.Status == DeploymentQueueTicketStatus.Scheduling ||
                             ticket.Status == DeploymentQueueTicketStatus.Scheduled ||
                             ticket.Status == DeploymentQueueTicketStatus.Running)
            .Select(ticket => ticket.SubjectConcurrencyKey)
            .Distinct()
            .ToArrayAsync(token);
        var blockedSubjects = activeSubjects.ToHashSet(StringComparer.Ordinal);
        window = window.Where(ticket => !blockedSubjects.Contains(ticket.SubjectConcurrencyKey)).ToArray();
        if (window.Length == 0)
            return [];

        var selected = window.Where(ticket => ticket.Operation != RuntimeOperationKind.Create)
            .Take(_options.SchedulingBatchSize).ToList();
        var remaining = Math.Max(0, _options.SchedulingBatchSize - selected.Count);
        if (remaining == 0)
            return selected.Select(ticket => ticket.Id).ToArray();

        var active = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(ticket => ticket.Operation == RuntimeOperationKind.Create &&
                             (ticket.Status == DeploymentQueueTicketStatus.Scheduling ||
                              ticket.Status == DeploymentQueueTicketStatus.Scheduled ||
                              ticket.Status == DeploymentQueueTicketStatus.Running))
            .Select(ticket => new { ticket.OwnerTeamId, ticket.OwnerUserId })
            .ToArrayAsync(token);
        var activeCounts = active.GroupBy(item => OwnerKey(item.OwnerTeamId, item.OwnerUserId))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var groups = window.Where(ticket => ticket.Operation == RuntimeOperationKind.Create)
            .GroupBy(ticket => OwnerKey(ticket.OwnerTeamId, ticket.OwnerUserId), StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => new Queue<DeploymentQueueTicket>(group.OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id)), StringComparer.Ordinal);

        while (remaining > 0 && groups.Count > 0)
        {
            var progressed = false;
            foreach (var owner in groups.Keys
                         .OrderBy(key => activeCounts.GetValueOrDefault(key))
                         .ThenBy(key => groups[key].Peek().CreatedAt)
                         .ThenBy(key => key, StringComparer.Ordinal)
                         .ToArray())
            {
                var queue = groups[owner];
                var ticket = queue.Peek();
                var limit = ticket.OwnerTeamId is not null
                    ? _options.MaxConcurrentCreatesPerTeam
                    : ticket.OwnerUserId is not null ? _options.MaxConcurrentCreatesPerUser : 1;
                if (activeCounts.GetValueOrDefault(owner) < Math.Max(1, limit))
                {
                    selected.Add(queue.Dequeue());
                    activeCounts[owner] = activeCounts.GetValueOrDefault(owner) + 1;
                    remaining--;
                    progressed = true;
                }
                if (queue.Count == 0)
                    groups.Remove(owner);
                if (remaining == 0)
                    break;
            }
            if (!progressed)
                break;
        }

        return selected.Select(ticket => ticket.Id).ToArray();
    }

    public static string OwnerKey(int? teamId, Guid? userId) => teamId is { } team
        ? $"team:{team}"
        : userId is { } user ? $"user:{user:D}" : "system";
}
