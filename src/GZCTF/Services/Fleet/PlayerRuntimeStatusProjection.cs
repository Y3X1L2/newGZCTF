using GZCTF.Models.Data;
using GZCTF.Models.Request.Shared;

namespace GZCTF.Services.Fleet;

public static class PlayerRuntimeStatusProjection
{
    public static void Apply(ClientFlagContext context, DeploymentQueueStatusModel? queue)
    {
        if (context.InstanceEntryStatus is not null ||
            queue is null ||
            queue.Operation != RuntimeOperationKind.Create)
            return;

        switch (queue.Status)
        {
            case DeploymentQueueTicketStatus.Pending:
            case DeploymentQueueTicketStatus.Scheduling:
            case DeploymentQueueTicketStatus.Scheduled:
            case DeploymentQueueTicketStatus.Running:
                context.InstanceEntryStatus = ContainerEntryStatus.Pending;
                break;
            case DeploymentQueueTicketStatus.Failed:
                context.InstanceEntryStatus = ContainerEntryStatus.Error;
                context.InstanceEntryError = FailureMessage(queue.Stage);
                break;
            case DeploymentQueueTicketStatus.Cancelled:
                context.InstanceEntryStatus = ContainerEntryStatus.Error;
                context.InstanceEntryError = "实例创建已取消，请重新创建。";
                break;
        }
    }

    static string FailureMessage(DeploymentStage stage) => stage switch
    {
        DeploymentStage.ImagePreparing or
        DeploymentStage.ImagePulling or
        DeploymentStage.ImageVerifying => "题目镜像暂不可用，请联系管理员。",
        DeploymentStage.AdmissionChecking or
        DeploymentStage.CapacityWaiting or
        DeploymentStage.NodeExecutionWaiting => "运行节点暂不可用，请稍后重试。",
        DeploymentStage.RuntimeNetworkApplying or
        DeploymentStage.AccessOpening => "实例网络入口配置失败，请稍后重试。",
        _ => "实例创建失败，请联系管理员或稍后重试。"
    };
}
