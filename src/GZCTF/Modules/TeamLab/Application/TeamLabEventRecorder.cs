using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabEventRecorder(
    AppDbContext context,
    IOperationalEventWriter events,
    OperationalCorrelation correlation)
{
    public TeamLabEvent Record(
        TeamLabRuntime runtime,
        string stage,
        TeamLabEventLevel level,
        string operationalEventCode,
        OperationalEventOutcome outcome,
        string message,
        OperationalError? error = null,
        Guid? workerNodeId = null,
        IReadOnlyDictionary<string, object?>? detail = null)
    {
        var trimmedMessage = Trim(message);
        var localEvent = new TeamLabEvent
        {
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            Stage = stage,
            Level = level,
            Message = trimmedMessage,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (context.Entry(runtime).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            context.TeamLabEvents.Add(localEvent);
        else
            runtime.Events.Add(localEvent);
        events.Append(new OperationalEventDraft(
            operationalEventCode,
            outcome,
            trimmedMessage,
            ToSeverity(level),
            correlation.Current ?? runtime.PublicId,
            error?.Category,
            error?.Code,
            error?.Retryable ?? false,
            detail ?? new Dictionary<string, object?>
            {
                ["generation"] = runtime.Generation,
                ["stage"] = stage,
                ["shardCount"] = runtime.Shards.Count(item => item.Generation == runtime.Generation),
                ["assetCount"] = runtime.Assets.Count(item => item.Generation == runtime.Generation)
            },
            OwnerUserId: runtime.CreatedById,
            WorkerNodeId: workerNodeId,
            TeamLabRuntimeId: runtime.Id,
            SubjectType: "teamlab-runtime",
            SubjectId: runtime.PublicId.ToString(),
            SubjectDisplayName: runtime.ExternalReference ?? runtime.PublicId.ToString(),
            ResourceType: "teamlab-runtime",
            ResourceId: runtime.PublicId.ToString(),
            ResourceDisplayName: runtime.ExternalReference ?? runtime.PublicId.ToString()));
        return localEvent;
    }

    private static OperationalEventSeverity ToSeverity(TeamLabEventLevel level) => level switch
    {
        TeamLabEventLevel.Warning => OperationalEventSeverity.Warning,
        TeamLabEventLevel.Error => OperationalEventSeverity.Error,
        _ => OperationalEventSeverity.Information
    };

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];
}
