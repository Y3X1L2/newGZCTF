namespace GZCTF.Services.Fleet;

public sealed record DeploymentExecutionContext(Guid TargetNodeId, bool CapacityReserved, Guid TicketId,
    int Generation = 1);

public class DeploymentExecutionContextAccessor
{
    public DeploymentExecutionContext? Current { get; private set; }

    public IDisposable Push(DeploymentExecutionContext context)
    {
        var previous = Current;
        Current = context;
        return new Popper(this, previous);
    }

    sealed class Popper : IDisposable
    {
        readonly DeploymentExecutionContextAccessor _accessor;
        readonly DeploymentExecutionContext? _previous;
        bool _disposed;

        public Popper(DeploymentExecutionContextAccessor accessor, DeploymentExecutionContext? previous)
        {
            _accessor = accessor;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _accessor.Current = _previous;
            _disposed = true;
        }
    }
}
