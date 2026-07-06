namespace GZCTF.Services.Fleet;

public class DeploymentQueueStateAccessor
{
    public DeploymentQueueStatusModel? LastQueuedDeployment { get; private set; }

    public void SetQueued(DeploymentQueueStatusModel? status) => LastQueuedDeployment = status;

    public DeploymentQueueStatusModel? ConsumeQueued()
    {
        var status = LastQueuedDeployment;
        LastQueuedDeployment = null;
        return status;
    }
}
