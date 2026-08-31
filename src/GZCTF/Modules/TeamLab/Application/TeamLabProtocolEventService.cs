using GZCTF.Models;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Persists a desensitized protocol event reported by a device/sensor simulator
/// into the runtime event stream (stage = "protocol"). Kept in the Application
/// layer so API controllers never touch persistence directly.
/// </summary>
public sealed class TeamLabProtocolEventService(
    AppDbContext context,
    TeamLabEventRecorder eventRecorder)
{
    public async Task<TeamLabProtocolEventResult> RecordAsync(
        Guid runtimeId,
        TeamLabProtocolEventReportModel model,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (runtime.Status != TeamLabRuntimeStatus.Running)
            throw new TeamLabApiContractException("runtime_not_running", "仅运行中运行时接受协议事件上报", 409);

        // Operational event detail is restricted to a desensitized allowlist. The
        // raw parameter values never leave the platform: only names and a count are
        // persisted so protocol events remain queryable without leaking telemetry.
        var parameterNames = model.Parameters?.Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray() ?? [];
        var detail = new Dictionary<string, object?>
        {
            ["protocolEventType"] = model.Type,
            ["protocolEventSource"] = model.Source,
            ["protocolEventOccurredAt"] = model.OccurredAt?.ToString("O"),
            ["protocolEventParameterCount"] = parameterNames.Length,
            ["protocolEventParameters"] = string.Join(",", parameterNames),
        };
        eventRecorder.Record(
            runtime,
            "protocol",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.ProtocolEvent,
            OperationalEventOutcome.Succeeded,
            "收到设备协议事件",
            detail: detail);
        await context.SaveChangesAsync(cancellationToken);
        return new TeamLabProtocolEventResult(runtimeId, "protocol", model.Type, model.Source);
    }
}

public sealed record TeamLabProtocolEventResult(Guid RuntimeId, string Stage, string Type, string Source);
