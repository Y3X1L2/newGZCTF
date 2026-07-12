namespace GZCTF.Modules.Runtime.Application;

public interface IDeploymentQueueWakeup
{
    ValueTask NotifyAsync(Guid ticketId, CancellationToken cancellationToken = default);
    ValueTask WaitAsync(TimeSpan maximumWait, CancellationToken cancellationToken = default);
}
