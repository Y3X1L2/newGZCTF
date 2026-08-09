namespace GZCTF.Modules.Runtime.Application;

public interface IRuntimeSignalWakeup
{
    ValueTask NotifyAsync(Guid operationId, CancellationToken cancellationToken = default);
    ValueTask WaitAsync(Guid operationId, TimeSpan maximumWait, CancellationToken cancellationToken = default);
}
