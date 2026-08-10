using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

public static class TeamLabFailurePresentation
{
    public static TeamLabFailureProjectionModel? ForRuntime(
        TeamLabRuntimeStatus status,
        DeploymentQueueTicket? ticket,
        Guid runtimeId)
    {
        var code = ticket?.ErrorCode ?? ticket?.BlockedReasonCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = status switch
            {
                TeamLabRuntimeStatus.Failed => "runtime_deployment_failed",
                TeamLabRuntimeStatus.CleanupPending => "runtime_cleanup_pending",
                _ => null
            };
        }

        return string.IsNullOrWhiteSpace(code)
            ? null
            : Create(
                code,
                ticket is null ? RuntimeStage(status) : Stage(ticket.Stage),
                ticket?.Retryable == true || status == TeamLabRuntimeStatus.CleanupPending,
                "runtime",
                runtimeId.ToString("D"));
    }

    public static TeamLabFailureProjectionModel? ForResource(
        TeamLabRuntimeStatus status,
        string stage,
        string code,
        string resourceType,
        string resourceId) =>
        status == TeamLabRuntimeStatus.Failed
            ? Create(code, stage, false, resourceType, resourceId)
            : null;

    public static IReadOnlyList<string> RecoveryActions(
        TeamLabRuntimeStatus status,
        TeamLabFailureProjectionModel? failure) => status switch
    {
        TeamLabRuntimeStatus.Paused => ["resume_runtime", "drain_runtime"],
        TeamLabRuntimeStatus.Running => ["pause_runtime", "drain_runtime"],
        TeamLabRuntimeStatus.CleanupPending => ["retry_cleanup", "drain_runtime"],
        TeamLabRuntimeStatus.Failed => failure?.Actions ?? ["rebuild_runtime", "drain_runtime"],
        TeamLabRuntimeStatus.Pending or TeamLabRuntimeStatus.Planning or
            TeamLabRuntimeStatus.Scheduled or TeamLabRuntimeStatus.Deploying or
            TeamLabRuntimeStatus.Probing => ["wait", "drain_runtime"],
        _ => []
    };

    public static string Stage(DeploymentStage stage) => stage switch
    {
        DeploymentStage.AdmissionChecking => "admission-checking",
        DeploymentStage.CapacityWaiting => "capacity-waiting",
        DeploymentStage.ImagePreparing => "image-preparing",
        DeploymentStage.ImagePulling => "image-pulling",
        DeploymentStage.ImageVerifying => "image-verifying",
        DeploymentStage.NodeExecutionWaiting => "node-execution-waiting",
        DeploymentStage.ContainerCreating => "container-creating",
        DeploymentStage.VmCreating => "vm-creating",
        DeploymentStage.RuntimeNetworkApplying => "runtime-network-applying",
        DeploymentStage.RuntimeAssetsCreating => "runtime-assets-creating",
        DeploymentStage.BootProbing => "boot-probing",
        DeploymentStage.AccessOpening => "access-opening",
        DeploymentStage.ArtifactsVerifying => "artifacts-verifying",
        DeploymentStage.NetworkApplying => "network-applying",
        DeploymentStage.RoutesApplying => "routes-applying",
        DeploymentStage.AssetBooting => "asset-booting",
        DeploymentStage.GuestWaiting => "guest-waiting",
        DeploymentStage.BootstrapInjecting => "bootstrap-injecting",
        DeploymentStage.BootstrapRunning => "bootstrap-running",
        DeploymentStage.GuestRebooting => "guest-rebooting",
        DeploymentStage.HealthProbing => "health-probing",
        DeploymentStage.ObservationStarting => "observation-starting",
        _ => stage.ToString().ToLowerInvariant()
    };

    private static TeamLabFailureProjectionModel Create(
        string code,
        string stage,
        bool retryable,
        string resourceType,
        string resourceId) => new(
            code,
            stage,
            retryable,
            Actions(code, retryable),
            resourceType,
            resourceId,
            Detail(code));

    private static IReadOnlyList<string> Actions(string code, bool retryable) => code switch
    {
        "resume_blocked" => ["wait_for_node", "rebuild_runtime", "drain_runtime"],
        "runtime_cleanup_pending" => ["retry_cleanup", "drain_runtime"],
        "image_distribution_failed" => ["retry_image_preparation", "drain_runtime"],
        "runtime_capacity_exhausted" => ["wait_for_capacity", "drain_runtime"],
        _ when retryable => ["retry_operation", "drain_runtime"],
        _ => ["rebuild_runtime", "drain_runtime"]
    };

    private static string Detail(string code) => code switch
    {
        "resume_blocked" => "原分配节点当前无法安全恢复该运行时。",
        "runtime_cleanup_pending" => "部分运行资源仍待清理。",
        "image_distribution_failed" => "所需镜像未能准备到目标节点。",
        "runtime_capacity_exhausted" => "当前节点容量不足。",
        "asset_deployment_failed" => "资产未能完成部署。",
        "shard_deployment_failed" => "分片未能完成部署。",
        _ => "运行操作未能完成，请按恢复动作处理。"
    };

    private static string RuntimeStage(TeamLabRuntimeStatus status) => status switch
    {
        TeamLabRuntimeStatus.CleanupPending => "cleanup-pending",
        TeamLabRuntimeStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant()
    };
}
