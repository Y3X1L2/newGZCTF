using GZCTF.Models.Data;

namespace GZCTF.Modules.Runtime.Application;

public interface IRuntimeTicketLifecycleObserver
{
    Task ProjectAsync(DeploymentQueueTicket ticket, CancellationToken cancellationToken);
}

public sealed class RuntimeTicketLifecycleDispatcher(
    IEnumerable<IRuntimeTicketLifecycleObserver> observers)
{
    public async Task ProjectAsync(
        DeploymentQueueTicket ticket,
        CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
            await observer.ProjectAsync(ticket, cancellationToken);
    }
}
