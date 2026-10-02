using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>Retains a safe, structured guest-network failure through the existing runtime queue.</summary>
public sealed class TeamLabGuestNetworkExecutionException : TeamLabRuntimeExecutionException, IOperationalFailureException
{
    private static readonly IReadOnlyDictionary<string, (OperationalErrorCategory Category, bool Retryable, string Stage, string Message)> Failures =
        new Dictionary<string, (OperationalErrorCategory, bool, string, string)>(StringComparer.Ordinal)
        {
            ["guest_qga_unavailable"] = (OperationalErrorCategory.AgentProtocol, false, "guest-ready", "来宾代理未就绪；请确认 QGA 和虚拟串口驱动已安装。"),
            ["guest_identity_conflict"] = (OperationalErrorCategory.Conflict, false, "guest", "虚拟机身份发生变化，网络配置已停止；请检查运行代次。"),
            ["guest_network_empty"] = (OperationalErrorCategory.Validation, false, "guest-ready", "平台管理网络缺少接口要求。"),
            ["guest_network_interface_missing"] = (OperationalErrorCategory.AgentProtocol, false, "guest-network-verify", "来宾未正确识别本次网卡；请检查网卡驱动。"),
            ["guest_network_drift"] = (OperationalErrorCategory.Network, true, "guest-network-verify", "来宾网络与当前计划不一致；请重新应用部署配置。"),
            ["guest_network_rollback_failed"] = (OperationalErrorCategory.Network, false, "guest-network-apply", "来宾网络配置失败，恢复原配置未能确认；请先检查保留的网络备份。"),
            ["guest_network_tools_unavailable"] = (OperationalErrorCategory.AgentProtocol, false, "guest", "Linux 网络组件缺失；请检查 cloud-init、Netplan、Python/PyYAML、iproute2 和 systemd-resolved。"),
            ["guest_network_apply_timeout"] = (OperationalErrorCategory.Network, true, "guest-network-apply", "来宾网络配置超时；请检查 QGA、驱动和网络工具。"),
            ["guest_network_apply_failed"] = (OperationalErrorCategory.Network, false, "guest-network-apply", "来宾网络配置失败；请检查 QGA、驱动和网络工具。"),
            ["guest_network_verify_timeout"] = (OperationalErrorCategory.Network, true, "guest-network-verify", "来宾实际 IP、DNS 或路由未能与平台配置一致。"),
            ["guest_network_timeout"] = (OperationalErrorCategory.AgentTransport, true, "guest", "来宾网络控制超时；请检查 QGA。"),
            ["guest_qga_stdin_unavailable"] = (OperationalErrorCategory.AgentProtocol, false, "guest", "来宾代理无法接收配置输入；请更新支持 guest-exec input-data 的 QGA。"),
            ["guest_network_control_failed"] = (OperationalErrorCategory.Network, false, "guest", "来宾网络配置或实际回读失败；请检查 QGA、驱动和网络组件。")
        };

    private TeamLabGuestNetworkExecutionException(string stage, OperationalError error, bool cleanupFailed)
        : base(error.Message + (cleanupFailed ? " 运行资源清理尚未完成，请检查清理状态。" : ""))
    {
        Stage = stage;
        Error = error;
    }

    public string Stage { get; }
    public OperationalError Error { get; }

    internal static TeamLabGuestNetworkExecutionException? FromAgent(string? stage, string? code, Guid workerNodeId)
    {
        if (stage is not ("guest-ready" or "guest-network-apply" or "guest-network-verify")) return null;
        // Guest/QGA error output can echo arguments. Preserve recognized codes, never raw script output.
        if (code is null || !Failures.ContainsKey(code)) code = "guest_network_control_failed";
        var failure = Failures[code];
        var error = new OperationalError(failure.Category, code, $"{stage}: {failure.Message}", failure.Retryable,
            WorkerNodeId: workerNodeId, Operation: $"teamlab.{stage}");
        return new(stage, error, false);
    }

    internal TeamLabGuestNetworkExecutionException WithCleanupFailure(bool cleanupFailed) => new(Stage, Error, cleanupFailed);

    internal static string? ProjectionStage(string code) => Failures.TryGetValue(code, out var failure) ? failure.Stage : null;
    internal static string? ProjectionDetail(string code) => Failures.TryGetValue(code, out var failure) ? failure.Message : null;
}
