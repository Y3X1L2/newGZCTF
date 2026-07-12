namespace GZCTF.Infrastructure.Concurrency;

public interface IDistributedLeaseProvider
{
    ValueTask<IDistributedLease> AcquireAsync(
        string resource,
        TimeSpan? waitTimeout = null,
        TimeSpan? leaseDuration = null,
        CancellationToken cancellationToken = default);
}
