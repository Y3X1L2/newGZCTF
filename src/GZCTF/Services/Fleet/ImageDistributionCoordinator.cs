using System.Threading.Channels;

namespace GZCTF.Services.Fleet;

public sealed class ImageDistributionCoordinator
{
    readonly Channel<bool> _wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Wake() => _wakeups.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan pollingInterval, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(pollingInterval);
        try
        {
            await _wakeups.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
        }
    }
}
