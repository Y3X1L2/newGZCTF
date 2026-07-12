namespace GZCTF.Infrastructure.Concurrency;

public interface IDistributedLease : IAsyncDisposable
{
    string Resource { get; }
    string OwnerToken { get; }
    CancellationToken LeaseLost { get; }
    ValueTask<bool> RenewAsync(CancellationToken cancellationToken = default);
}
