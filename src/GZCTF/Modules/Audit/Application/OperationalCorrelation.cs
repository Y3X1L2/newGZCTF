namespace GZCTF.Modules.Audit.Application;

public sealed class OperationalCorrelation
{
    private Guid? _correlationId;

    public Guid? Current => _correlationId;

    public Guid Ensure()
    {
        _correlationId ??= Guid.CreateVersion7();
        return _correlationId.Value;
    }

    public void Promote(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation id cannot be empty.", nameof(correlationId));
        _correlationId = correlationId;
    }

    public IDisposable Begin(Guid? correlationId = null)
    {
        var previous = _correlationId;
        _correlationId = correlationId is { } value && value != Guid.Empty
            ? value
            : Guid.CreateVersion7();
        return new Scope(this, previous);
    }

    private sealed class Scope(OperationalCorrelation owner, Guid? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            owner._correlationId = previous;
            _disposed = true;
        }
    }
}
