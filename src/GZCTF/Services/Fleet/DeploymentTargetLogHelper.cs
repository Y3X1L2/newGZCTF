using GZCTF.Models.Data;

namespace GZCTF.Services.Fleet;

public static class DeploymentTargetLogHelper
{
    public static (string Message, TaskStatus Status, LogLevel Level) Build(
        string stage, DeploymentTarget target, WorkerNode? node = null, string? detail = null)
    {
        var status = ToTaskStatus(target.Status);
        var level = target.Status == TargetStatus.Failed ? LogLevel.Warning : LogLevel.Information;
        var nodeLabel = ResolveNodeLabel(target, node);
        var result = ResolveResult(target);
        var error = string.IsNullOrWhiteSpace(target.ErrorMessage) ? null : $" error={target.ErrorMessage}";
        var extra = string.IsNullOrWhiteSpace(detail) ? null : $" {detail}";

        return (
            $"Deployment target {target.Id} {stage}: {target.Type}/{target.Action} status={target.Status}{nodeLabel}{result}{error}{extra}",
            status,
            level);
    }

    public static void SystemLogDeploymentTarget<T>(
        this ILogger<T> logger, string stage, DeploymentTarget? target, WorkerNode? node = null, string? detail = null)
    {
        if (target is null)
            return;

        var (message, status, level) = Build(stage, target, node, detail);
        logger.SystemLog(message, status, level);
    }

    static TaskStatus ToTaskStatus(TargetStatus status) =>
        status switch
        {
            TargetStatus.Completed => TaskStatus.Success,
            TargetStatus.Failed => TaskStatus.Failed,
            TargetStatus.Cancelled => TaskStatus.Exit,
            _ => TaskStatus.Pending
        };

    static string ResolveNodeLabel(DeploymentTarget target, WorkerNode? node)
    {
        if (node is null && target.TargetNodeId is null)
            return string.Empty;

        var id = node?.Id ?? target.TargetNodeId;
        var name = string.IsNullOrWhiteSpace(node?.Name) ? null : node.Name;
        var host = string.IsNullOrWhiteSpace(node?.HostAddress) ? null : node.HostAddress;
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(name))
            parts.Add($"name={name}");
        if (!string.IsNullOrWhiteSpace(host))
            parts.Add($"host={host}");
        if (id.HasValue)
            parts.Add($"id={id}");

        return parts.Count == 0 ? string.Empty : $" node=({string.Join(", ", parts)})";
    }

    static string ResolveResult(DeploymentTarget target)
    {
        var hasHost = !string.IsNullOrWhiteSpace(target.ResultHost);
        var hasPort = target.ResultPort is > 0;

        if (!hasHost && !hasPort)
            return string.Empty;

        return hasHost && hasPort
            ? $" result={target.ResultHost}:{target.ResultPort}"
            : $" result={target.ResultHost ?? target.ResultPort?.ToString()}";
    }
}
