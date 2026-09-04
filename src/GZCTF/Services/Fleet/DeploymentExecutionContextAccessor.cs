namespace GZCTF.Services.Fleet;

public sealed record DeploymentExecutionContext(Guid TargetNodeId, bool CapacityReserved, Guid TicketId,
    int Generation = 1);

public class DeploymentExecutionContextAccessor
{
    readonly AsyncLocal<DeploymentExecutionContext?> _current = new();

    public DeploymentExecutionContext? Current => _current.Value;

    public IDisposable Push(DeploymentExecutionContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
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

            _accessor._current.Value = _previous;
            _disposed = true;
        }
    }
}
