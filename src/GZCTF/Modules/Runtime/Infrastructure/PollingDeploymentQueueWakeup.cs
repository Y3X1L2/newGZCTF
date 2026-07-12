using GZCTF.Modules.Runtime.Application;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class PollingDeploymentQueueWakeup : IDeploymentQueueWakeup
{
    public ValueTask NotifyAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public async ValueTask WaitAsync(TimeSpan maximumWait, CancellationToken cancellationToken = default) =>
        await Task.Delay(maximumWait, cancellationToken);
}
